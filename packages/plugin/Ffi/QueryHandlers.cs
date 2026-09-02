using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Models;
using MusicBeePlugin.Settings;
using MusicBeePlugin.Ffi.Generated;
using MusicBeePlugin.Utilities;

namespace MusicBeePlugin.Ffi
{
    /// <summary>
    ///     The read side of the FFI RPC: maps a Rust <c>query_data</c> request
    ///     (a <see cref="QueryType"/> id + MessagePack params) to a provider call
    ///     and builds the canonical DTO reply. FFI-free (works with byte[]), so it
    ///     unit-tests against mock providers with no P/Invoke. The caller
    ///     (<see cref="NativeBridge"/>) serializes access under the API lock.
    /// </summary>
    internal sealed class QueryHandlers
    {
        private readonly IPlayerDataProvider _player;
        private readonly ITrackDataProvider _track;
        private readonly IPlaylistDataProvider _playlist;
        private readonly ILibraryDataProvider _library;
        private readonly IUserSettings _userSettings;

        public QueryHandlers(
            IPlayerDataProvider player,
            ITrackDataProvider track,
            IPlaylistDataProvider playlist,
            ILibraryDataProvider library,
            IUserSettings userSettings)
        {
            _player = player;
            _track = track;
            _playlist = playlist;
            _library = library;
            _userSettings = userSettings;
        }

        /// <summary>
        ///     Serve a query. Returns the MessagePack reply, or null if the query
        ///     type is unknown (the caller reports an error status).
        /// </summary>
        public byte[] Handle(int queryType, byte[] p)
        {
            switch ((QueryType)queryType)
            {
                case QueryType.PlayerState: return Pack(BuildPlayerState());
                case QueryType.TrackInfo: return Pack(BuildTrackInfo());
                case QueryType.CoverData: return Pack(BuildCover());
                case QueryType.Lyrics: return Pack(BuildLyrics());
                case QueryType.NowPlayingLyricsSynced: return Pack(BuildSyncedLyrics());
                case QueryType.HasLastFmAccount: return Pack(_player.HasLastFmAccount());
                case QueryType.NowPlayingDetails: return Pack(BuildTrackDetails());
                case QueryType.PlaybackPosition: return Pack(BuildPlaybackPosition());
                case QueryType.OutputDevices: return Pack(BuildOutputDevices());
                case QueryType.NowPlayingRating: return Pack(_track.GetNowPlayingRating() ?? string.Empty);
                case QueryType.NowPlayingLfmRating: return Pack(_track.GetNowPlayingLastfmStatus().ToString());
                case QueryType.PluginVersion: return Pack(_userSettings.CurrentVersion ?? string.Empty);
                case QueryType.PlaylistList: return Pack(BuildPlaylists(Page(p)));
                case QueryType.NowPlayingList: return Pack(BuildNowPlayingList(Page(p), ordered: false));
                case QueryType.NowPlayingListOrdered: return Pack(BuildNowPlayingList(Page(p), ordered: true));
                case QueryType.RadioStations: return Pack(BuildRadioStations(Page(p)));
                case QueryType.LibraryBrowseGenres: return Pack(BuildBrowseGenres(Page(p)));
                case QueryType.LibraryBrowseArtists: return Pack(BuildBrowseArtists(Msgpack.Deserialize<BrowseParams>(p)));
                case QueryType.LibraryBrowseAlbums: return Pack(BuildBrowseAlbums(Page(p)));
                case QueryType.LibraryBrowseTracks: return Pack(BuildBrowseTracks(Page(p)));
                case QueryType.LibraryGenreArtists: return Pack(BuildGenreArtists(Q(p)));
                case QueryType.LibraryArtistAlbums: return Pack(BuildArtistAlbums(Q(p)));
                case QueryType.LibraryAlbumTracks: return Pack(BuildAlbumTracks(Q(p)));
                case QueryType.AlbumIdentifiers: return Pack(BuildAlbumIdentifiers());
                case QueryType.ArtworkRawForPath: return Pack(BuildArtworkRaw(Msgpack.Deserialize<PathParams>(p)));
                case QueryType.BatchMetadata: return Pack(BuildBatchMetadata(Msgpack.Deserialize<BatchMetadataParams>(p)));
                case QueryType.LibraryTrackPaths: return Pack(_library.GetAllTrackPaths());
                case QueryType.LibraryTracksForPaths: return Pack(_library.GetTracksForPaths(Msgpack.Deserialize<PathsParams>(p).paths));
                case QueryType.LibraryTrackTags: return Pack(_library.GetTrackTags(Msgpack.Deserialize<PathsParams>(p).paths));
                case QueryType.LibrarySyncDelta: return Pack(BuildSyncDelta(Msgpack.Deserialize<SyncDeltaParams>(p)));
                default: return null;
            }
        }

        private PlayerState BuildPlayerState() => new PlayerState
        {
            play_state = _player.GetPlayState().ToString(),
            volume = _player.GetVolume(),
            mute = _player.GetMute(),
            shuffle = _player.GetShuffleMode().ToString(),
            repeat = _player.GetRepeatMode().ToString(),
            position = _player.GetPosition(),
            scrobble = _player.GetScrobbleEnabled(),
        };

        private TrackInfo BuildTrackInfo() => _track.GetNowPlayingTrackInfo() ?? new TrackInfo();

        /// <summary>
        ///     The now-playing artwork as MusicBee holds it: embedded art if
        ///     there is any, otherwise downloaded art. Status is 200 when
        ///     something was found and 404 when nothing was; the bytes are
        ///     unresized, because the core owns sizing and caching.
        /// </summary>
        private Cover BuildCover()
        {
            // A thin API read, matching the retired
            // CoverService.GetNowPlayingCover.
            var art = _track.GetNowPlayingArtwork();
            if (string.IsNullOrEmpty(art))
                art = _track.GetNowPlayingDownloadedArtwork();
            art = art ?? string.Empty;
            return new Cover { status = art.Length > 0 ? 200 : 404, cover = art };
        }

        private Lyrics BuildLyrics()
        {
            var text = _track.GetNowPlayingLyrics() ?? string.Empty;
            return new Lyrics { status = text.Length > 0 ? 200 : 404, lyrics = text };
        }

        private Lyrics BuildSyncedLyrics()
        {
            var text = _track.GetSyncedLyrics() ?? string.Empty;
            return new Lyrics { status = text.Length > 0 ? 200 : 404, lyrics = text };
        }

        private TrackDetails BuildTrackDetails() => _track.GetNowPlayingTrackDetails() ?? new TrackDetails();

        private PlaybackPositionResponse BuildPlaybackPosition() =>
            _track.GetPlaybackPosition() ?? new PlaybackPositionResponse();

        private OutputDevices BuildOutputDevices() => _player.GetOutputDevices() ?? new OutputDevices();

        private Page<Playlist> BuildPlaylists(PaginationParams p) =>
            Paginate(_playlist.GetPlaylists(), p.offset, p.limit);

        /// <summary>
        ///     A page of the now-playing list. <paramref name="ordered" /> picks
        ///     the variant: the "ordered" walk runs from the current track
        ///     forward and reports the length of that walk as its total, while
        ///     the sequential one pages the whole queue and reports the real
        ///     count. The two totals mean different things - see below.
        /// </summary>
        private Page<NowPlayingListTrack> BuildNowPlayingList(PaginationParams p, bool ordered)
        {
            // Platform never crosses the FFI: the core asks for "ordered" (iOS,
            // anchored at the current track) or the sequential page (Android).
            // Tags are read only for the requested window, and the list is live,
            // so it is never cached.
            var data = (ordered
                ? _track.GetNowPlayingListOrdered(p.offset, p.limit)
                : _track.GetNowPlayingListPage(p.offset, p.limit)).ToList();
            // The ordered walk stops at the end of the queue, so its total is the
            // walk's length, not the full-queue count. Reporting the latter ships
            // total=N beside fewer than N items, and an iOS client sizing its list
            // off total then indexes past the end.
            return new Page<NowPlayingListTrack>
            {
                offset = p.offset,
                limit = p.limit,
                total = ordered ? data.Count : _track.GetNowPlayingListCount(),
                data = data,
            };
        }

        private Page<RadioStation> BuildRadioStations(PaginationParams p) =>
            Paginate(_library.GetRadioStations(0, FetchAll), p.offset, p.limit);

        private Page<GenreData> BuildBrowseGenres(PaginationParams p) =>
            Paginate(_library.BrowseGenres(0, FetchAll), p.offset, p.limit);

        private Page<ArtistData> BuildBrowseArtists(BrowseParams p) =>
            Paginate(_library.BrowseArtists(0, FetchAll, p.album_artists), p.offset, p.limit);

        private Page<AlbumData> BuildBrowseAlbums(PaginationParams p) =>
            Paginate(_library.BrowseAlbums(0, FetchAll), p.offset, p.limit);

        private Page<Track> BuildBrowseTracks(PaginationParams p) =>
            Paginate(_library.BrowseTracks(0, FetchAll), p.offset, p.limit);

        private List<ArtistData> BuildGenreArtists(QueryParams p) =>
            (_library.GetGenreArtists(p.query ?? string.Empty, Source()) ?? Enumerable.Empty<ArtistData>()).ToList();

        private List<AlbumData> BuildArtistAlbums(QueryParams p) =>
            _library.GetArtistAlbums(p.query ?? string.Empty, Source()) ?? new List<AlbumData>();

        private List<Track> BuildAlbumTracks(QueryParams p) =>
            (_library.GetAlbumTracks(p.query ?? string.Empty, Source()) ?? Enumerable.Empty<Track>()).ToList();

        // Cover-cache leaf providers: the host supplies raw ingredients, the Rust
        // core owns resize/hash/cache/serve. See ILibraryDataProvider for why the
        // single-pass GetAlbumIdentities replaces the old three-scan preparation.
        private List<AlbumIdentifier> BuildAlbumIdentifiers() =>
            _library.GetAlbumIdentities()
                .Select(a => new AlbumIdentifier
                {
                    artist = a.Artist ?? string.Empty,
                    album = a.Album ?? string.Empty,
                    path = a.Path ?? string.Empty,
                    modified = a.Modified,
                }).ToList();

        private string BuildArtworkRaw(PathParams p)
        {
            var data = _library.GetArtworkDataForTrack(p.path ?? string.Empty);
            return data?.Length > 0 ? Convert.ToBase64String(data) : string.Empty;
        }

        private List<TrackMetadata> BuildBatchMetadata(BatchMetadataParams p) =>
            _library.GetBatchTrackMetadata(p.paths ?? Enumerable.Empty<string>())
                .Select(kv => new TrackMetadata
                {
                    path = kv.Key,
                    artist = kv.Value.Artist ?? string.Empty,
                    album = kv.Value.Album ?? string.Empty,
                }).ToList();

        private SyncDelta BuildSyncDelta(SyncDeltaParams p)
        {
            var (added, updated, deleted) = _library.GetSyncDelta(p.updated_since);
            return new SyncDelta { added = added, updated = updated, deleted = deleted };
        }

        private static byte[] Pack<T>(T value) => Msgpack.Serialize(value);
        private static PaginationParams Page(byte[] p) => Msgpack.Deserialize<PaginationParams>(p);
        private static QueryParams Q(byte[] p) => Msgpack.Deserialize<QueryParams>(p);

        private SearchSource Source() => SearchSourceHelper.GetSearchSource(_userSettings);

        /// <summary>
        ///     Pass to a provider's (offset, limit) to request the entire
        ///     collection. MusicBee's lookup/list APIs can't page at the source,
        ///     so we fetch all and slice here (matching the shipped plugin).
        /// </summary>
        private const int FetchAll = int.MaxValue;

        /// <summary>
        ///     Materialize the full result, slice to the requested page, and
        ///     report the true total. This is the shipped PagedResponseHelper
        ///     semantics: <c>total</c> is the full count, not an
        ///     <c>offset + pageCount</c> approximation (which broke paging past
        ///     the first page). <c>limit &lt;= 0</c> means "the rest from offset".
        ///     Providers now yield the DTO directly, so there is no per-item map.
        /// </summary>
        private static Page<T> Paginate<T>(IEnumerable<T> source, int offset, int limit)
        {
            var all = (source ?? Enumerable.Empty<T>()).ToList();
            var take = limit > 0 ? limit : all.Count;
            var data = all.Skip(offset).Take(take).ToList();
            return new Page<T> { offset = offset, limit = limit, total = all.Count, data = data };
        }
    }
}
