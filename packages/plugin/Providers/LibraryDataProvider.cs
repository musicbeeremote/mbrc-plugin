using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Models;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Ffi.Generated;
using MusicBeePlugin.Utilities;

namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Data provider implementation for music library operations.
    ///     Contains all MusicBee-specific API logic including query lifecycle,
    ///     null separator parsing, filter building, and type mappings.
    ///     Uses yield return for streaming results and proper cleanup via try/finally.
    /// </summary>
    public class LibraryDataProvider : ILibraryDataProvider
    {
        private static readonly string[] DoubleSeparator = { "\0\0" };
        private static readonly char[] NullSeparator = { '\0' };
        private static readonly string[] GenreSearchFields = { "Genre" };
        private static readonly string[] ArtistSearchFields = { "ArtistPeople" };
        private static readonly string[] AlbumSearchFields = { "Album" };
        private static readonly string[] MultiValueSeparator = { "; " };

        // The tags a banned track is matched on when a drill-down adds it back,
        // standing in for the search fields above. ArtistPeople names track artists only.
        private static readonly Plugin.MetaDataType[] GenreMatchFields = { Plugin.MetaDataType.Genre };
        private static readonly Plugin.MetaDataType[] AlbumMatchFields = { Plugin.MetaDataType.Album };
        private static readonly Plugin.MetaDataType[] ArtistMatchFields =
        {
            Plugin.MetaDataType.Artist, Plugin.MetaDataType.Artists,
        };

        // A banned track's artists as the artist lookup table counts them: the
        // split artist-role credits, or the artist tag when nobody split it.
        private static readonly Plugin.MetaDataType[] ArtistCreditFields =
        {
            Plugin.MetaDataType.ArtistsWithArtistRole, Plugin.MetaDataType.Artist,
        };

        // Tag sets read in one bulk Library_GetFileTags call per track (instead of
        // one MusicBee API round-trip per tag). Index order is consumed below.
        private static readonly Plugin.MetaDataType[] BrowseTrackFields =
        {
            Plugin.MetaDataType.Artist, Plugin.MetaDataType.TrackTitle, Plugin.MetaDataType.Album,
            Plugin.MetaDataType.AlbumArtist, Plugin.MetaDataType.Genre,
            Plugin.MetaDataType.TrackNo, Plugin.MetaDataType.DiscNo,
        };

        // The V6 typed-track tags: the browse fields plus the raw extended metadata
        // tags the core parses (Year/OriginalYear/Rating). Duration and DateAdded are file
        // properties (read via GetFileProperty), not metadata tags. Index order is
        // consumed by GetTrackTags.
        private static readonly Plugin.MetaDataType[] TrackTagFields =
        {
            Plugin.MetaDataType.Artist, Plugin.MetaDataType.TrackTitle, Plugin.MetaDataType.Album,
            Plugin.MetaDataType.AlbumArtist, Plugin.MetaDataType.Genre,
            Plugin.MetaDataType.TrackNo, Plugin.MetaDataType.DiscNo,
            Plugin.MetaDataType.Year, Plugin.MetaDataType.Rating,
            Plugin.MetaDataType.MultiArtist, Plugin.MetaDataType.MultiComposer,
            Plugin.MetaDataType.OriginalYear,
        };

        private static readonly Plugin.MetaDataType[] AlbumTrackFields =
        {
            Plugin.MetaDataType.Artist, Plugin.MetaDataType.TrackTitle, Plugin.MetaDataType.AlbumArtist,
            Plugin.MetaDataType.TrackNo, Plugin.MetaDataType.DiscNo,
        };

        private static readonly Plugin.MetaDataType[] ArtistAlbumFields =
        {
            Plugin.MetaDataType.AlbumArtist, Plugin.MetaDataType.Album,
        };

        private readonly Plugin.MusicBeeApiInterface _api;
        private readonly object _excludedLock = new object();
        private List<string> _excluded;
        private int _excludedGeneration;

        public LibraryDataProvider(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        #region Radio Stations

        public List<RadioStation> GetRadioStations(int offset = 0, int limit = 4000)
        {
            var radioStations = Array.Empty<string>();
            var success = _api.Library_QueryFilesEx("domain=Radio", out radioStations);

            List<RadioStation> stations;
            if (success)
                stations = radioStations.Select(s => new RadioStation
                {
                    url = s ?? string.Empty,
                    name = _api.Library_GetFileTag(s, Plugin.MetaDataType.TrackTitle) ?? string.Empty
                }).ToList();
            else
                stations = new List<RadioStation>();

            return stations;
        }

        #endregion

        #region Browse Operations

        public IEnumerable<GenreData> BrowseGenres(int offset = 0, int limit = 4000)
        {
            if (!_api.Library_QueryLookupTable("genre", "count", null))
            {
                _api.Library_QueryLookupTable(null, null, null);
                yield break;
            }

            try
            {
                var rawValue = _api.Library_QueryGetLookupTableValue(null);
                foreach (var entry in rawValue.Split(DoubleSeparator, StringSplitOptions.None))
                {
                    var genreInfo = entry.Split(NullSeparator, StringSplitOptions.None);
                    if (genreInfo.Length >= 2)
                    {
                        yield return new GenreData
                        {
                            genre = genreInfo[0].Cleanup() ?? string.Empty,
                            count = int.Parse(genreInfo[1], CultureInfo.InvariantCulture),
                        };
                    }
                }
            }
            finally
            {
                _api.Library_QueryLookupTable(null, null, null);
            }
        }

        public IEnumerable<ArtistData> BrowseArtists(int offset = 0, int limit = 4000, bool albumArtists = false)
        {
            var artistType = albumArtists ? "albumartist" : "artist";

            if (!_api.Library_QueryLookupTable(artistType, "count", null))
            {
                _api.Library_QueryLookupTable(null, null, null);
                yield break;
            }

            try
            {
                var rawValue = _api.Library_QueryGetLookupTableValue(null);
                foreach (var entry in rawValue.Split(DoubleSeparator, StringSplitOptions.None))
                {
                    var artistInfo = entry.Split(NullSeparator);
                    if (artistInfo.Length >= 2)
                    {
                        yield return new ArtistData
                        {
                            artist = artistInfo[0].Cleanup() ?? string.Empty,
                            count = int.Parse(artistInfo[1], CultureInfo.InvariantCulture),
                        };
                    }
                }
            }
            finally
            {
                _api.Library_QueryLookupTable(null, null, null);
            }
        }

        /// <summary>
        ///     Albums in browse order, deduplicated on (album artist, album),
        ///     first occurrence wins. Every entry's count is 1 - this list is for
        ///     browsing, not for reporting track counts (see GetArtistAlbums for
        ///     those).
        /// </summary>
        public List<AlbumData> BrowseAlbums(int offset = 0, int limit = 4000)
        {
            // A first-wins HashSet keyed on artist\0album reproduces what
            // AlbumData's IEquatable + Distinct did, so ordering and count match.
            // Count stays 1 per entry: this is a browse list, not a tally.
            var albums = new List<AlbumData>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", null))
                try
                {
                    var entries = _api.Library_QueryGetLookupTableValue(null)
                        .Split(DoubleSeparator, StringSplitOptions.None)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Select(s => s.Trim());

                    foreach (var entry in entries)
                    {
                        var album = CreateAlbum(entry);
                        var key = album.artist + "\0" + album.album;
                        if (seen.Add(key))
                            albums.Add(album);
                    }
                }
                catch (IndexOutOfRangeException)
                {
                    // Handle exception silently like in original code
                }

            _api.Library_QueryLookupTable(null, null, null);
            return albums;
        }

        public IEnumerable<Track> BrowseTracks(int offset = 0, int limit = 4000)
        {
            if (!_api.Library_QueryFiles(null))
                yield break;

            while (true)
            {
                var currentTrack = _api.Library_QueryGetNextFile();
                if (string.IsNullOrEmpty(currentTrack))
                    break;

                _api.Library_GetFileTags(currentTrack, BrowseTrackFields, out var tags);

                yield return new Track
                {
                    artist = Tag(tags, 0).Cleanup(),
                    title = Tag(tags, 1).Cleanup(),
                    album = Tag(tags, 2).Cleanup(),
                    album_artist = Tag(tags, 3).Cleanup(),
                    genre = Tag(tags, 4).Cleanup(),
                    trackno = ParseTag(tags, 5),
                    disc = ParseTag(tags, 6),
                    src = currentTrack
                };
            }
        }

        #endregion

        #region Hierarchical Navigation

        /// <summary>
        ///     The tracks of one album, banned ones included, in disc and track order
        ///     once a banned track had to be added back.
        /// </summary>
        public IEnumerable<Track> GetAlbumTracks(string album)
        {
            var banned = ExcludedWhere(AlbumMatchFields, album, false);
            var tracks = WalkFilter(XmlFilterHelper.CreateFilter(
                    AlbumSearchFields, album, true, LibrarySource.Library))
                .Select(path => ReadAlbumTrack(path, string.Empty))
                .ToList();
            if (banned.Count == 0)
                return tracks;

            tracks.AddRange(banned.Select(path => ReadAlbumTrack(path, string.Empty)));
            return InAlbumOrder(tracks);
        }

        /// <summary>
        ///     Tracks grouped by album artist, in the order each first appears, then
        ///     by disc and track, so two albums sharing a name stay apart.
        /// </summary>
        private static List<Track> InAlbumOrder(List<Track> tracks)
        {
            var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in tracks)
            {
                var albumArtist = track.album_artist ?? string.Empty;
                if (!groups.ContainsKey(albumArtist))
                    groups[albumArtist] = groups.Count;
            }

            return tracks
                .OrderBy(t => groups[t.album_artist ?? string.Empty])
                .ThenBy(t => t.disc)
                .ThenBy(t => t.trackno)
                .ToList();
        }

        /// <summary>
        ///     Every track filed under one genre, banned ones included.
        /// </summary>
        /// <remarks>
        ///     Walking the genre's artists and their albums cannot answer this:
        ///     GetAlbumTracks leaves the genre field empty, so an artist filed
        ///     under two genres yields tracks nothing can tell apart. Asking the
        ///     library for the genre directly is both exact and one query.
        /// </remarks>
        public IEnumerable<Track> GetGenreTracks(string genre)
        {
            var banned = ExcludedWhere(GenreMatchFields, genre, true);
            return WalkFilter(XmlFilterHelper.CreateFilter(
                    GenreSearchFields, genre, true, LibrarySource.Library))
                .Concat(banned)
                .Select(path => ReadAlbumTrack(path, genre))
                .ToList();
        }

        /// <summary>
        ///     One entry per (album artist, album) for the given artist, each
        ///     carrying its track count, banned tracks included. Unlike
        ///     BrowseAlbums, the count here is real.
        /// </summary>
        public List<AlbumData> GetArtistAlbums(string artist)
        {
            var banned = ExcludedWhere(ArtistMatchFields, artist, true);
            var files = WalkFilter(XmlFilterHelper.CreateFilter(
                ArtistSearchFields, artist, true, LibrarySource.Library));

            var albums = new Dictionary<string, AlbumData>(StringComparer.Ordinal);
            foreach (var file in files.Concat(banned))
            {
                _api.Library_GetFileTags(file, ArtistAlbumFields, out var tags);
                var albumArtist = Tag(tags, 0);
                var album = Tag(tags, 1);
                var key = albumArtist + "\0" + album;

                if (albums.TryGetValue(key, out var existing))
                    existing.count++;
                else
                    albums[key] = new AlbumData { artist = albumArtist, album = album, count = 1 };
            }

            return albums.Values.ToList();
        }

        /// <summary>
        ///     The artists with tracks in one genre and how many each has, banned
        ///     tracks included.
        /// </summary>
        public IEnumerable<ArtistData> GetGenreArtists(string genre)
        {
            var banned = ExcludedWhere(GenreMatchFields, genre, true);
            var artists = LookupGenreArtists(genre);
            foreach (var name in banned.SelectMany(CreditedArtists))
            {
                var known = artists.FirstOrDefault(
                    a => string.Equals(a.artist, name, StringComparison.OrdinalIgnoreCase));
                if (known != null)
                    known.count++;
                else
                    artists.Add(new ArtistData { artist = name, count = 1 });
            }

            return artists;
        }

        private IEnumerable<string> CreditedArtists(string path)
        {
            _api.Library_GetFileTags(path, ArtistCreditFields, out var tags);
            var credits = Tag(tags, 0);
            return (credits.Length > 0 ? credits : Tag(tags, 1))
                .Split(MultiValueSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     The music library tracks MusicBee's filtered queries leave out, which
        ///     are its banned ones. Empty when either listing fails.
        /// </summary>
        /// <remarks>
        ///     Kept until <see cref="ForgetExcludedPaths" />: a scope queue walks one
        ///     drill-down per album, and each would otherwise list the library twice.
        /// </remarks>
        public List<string> GetExcludedPaths()
        {
            int generation;
            lock (_excludedLock)
            {
                if (_excluded != null)
                    return _excluded;
                generation = _excludedGeneration;
            }

            var excluded = ListExcludedPaths();
            if (excluded == null)
                return new List<string>();

            lock (_excludedLock)
            {
                if (generation == _excludedGeneration)
                    _excluded = excluded;
            }

            return excluded;
        }

        /// <summary>Drops the kept excluded paths, for a library change MusicBee reported.</summary>
        public void ForgetExcludedPaths()
        {
            lock (_excludedLock)
            {
                _excluded = null;
                _excludedGeneration++;
            }
        }

        private List<string> ListExcludedPaths()
        {
            if (!_api.Library_QueryFilesEx(null, out var all) || all == null)
                return null;
            if (!_api.Library_QueryFilesEx(XmlFilterHelper.AllOf(LibrarySource.Library), out var kept) || kept == null)
                return null;

            var listed = new HashSet<string>(kept, StringComparer.OrdinalIgnoreCase);
            return all.Where(path => !listed.Contains(path)).ToList();
        }

        /// <summary>
        ///     The excluded tracks a drill-down's <c>Is</c> filter would match on
        ///     <paramref name="fields" />, each <c>; </c>-joined value counting when
        ///     the fields are <paramref name="multiValue" />.
        /// </summary>
        /// <remarks>
        ///     Called before the drill-down's own walk: a listing in the middle of a
        ///     Library_QueryFiles walk would reset its cursor.
        /// </remarks>
        private List<string> ExcludedWhere(Plugin.MetaDataType[] fields, string value, bool multiValue)
        {
            var wanted = value ?? string.Empty;
            var matches = new List<string>();
            foreach (var path in GetExcludedPaths())
            {
                _api.Library_GetFileTags(path, fields, out var tags);
                if (Enumerable.Range(0, fields.Length).Any(i => Holds(Tag(tags, i), wanted, multiValue)))
                    matches.Add(path);
            }

            return matches;
        }

        private static bool Holds(string tag, string wanted, bool multiValue)
        {
            return string.Equals(tag, wanted, StringComparison.OrdinalIgnoreCase)
                   || (multiValue && tag.Split(MultiValueSeparator, StringSplitOptions.None)
                       .Any(v => string.Equals(v, wanted, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Every file a filter selects, from one Library_QueryFiles walk.</summary>
        private List<string> WalkFilter(string filter)
        {
            var files = new List<string>();
            if (!_api.Library_QueryFiles(filter))
                return files;

            while (true)
            {
                var current = _api.Library_QueryGetNextFile();
                if (string.IsNullOrEmpty(current))
                    break;
                files.Add(current);
            }

            return files;
        }

        /// <summary>
        ///     A drill-down track: album is left empty and genre is the one asked
        ///     for, as the V4 DTO has always carried them.
        /// </summary>
        private Track ReadAlbumTrack(string path, string genre)
        {
            _api.Library_GetFileTags(path, AlbumTrackFields, out var tags);
            return new Track
            {
                artist = Tag(tags, 0),
                title = Tag(tags, 1),
                album_artist = Tag(tags, 2),
                trackno = ParseTag(tags, 3),
                disc = ParseTag(tags, 4),
                src = path,
                album = string.Empty,
                genre = genre
            };
        }

        private List<ArtistData> LookupGenreArtists(string genre)
        {
            var artists = new List<ArtistData>();
            var filter = XmlFilterHelper.CreateFilter(
                GenreSearchFields, genre, true, LibrarySource.Library);

            if (!_api.Library_QueryLookupTable("artist", "count", filter))
            {
                _api.Library_QueryLookupTable(null, null, null);
                return artists;
            }

            try
            {
                var rawValue = _api.Library_QueryGetLookupTableValue(null);
                foreach (var entry in rawValue.Split(DoubleSeparator, StringSplitOptions.None))
                {
                    var artistInfo = entry.Split(NullSeparator);
                    if (artistInfo.Length >= 2)
                    {
                        artists.Add(new ArtistData
                        {
                            artist = artistInfo[0] ?? string.Empty,
                            count = int.Parse(artistInfo[1], CultureInfo.InvariantCulture),
                        });
                    }
                }
            }
            finally
            {
                _api.Library_QueryLookupTable(null, null, null);
            }

            return artists;
        }

        #endregion

        #region Artwork

        public byte[] GetArtworkDataForTrack(string trackPath)
        {
            var locations = Plugin.PictureLocations.EmbedInFile |
                            Plugin.PictureLocations.LinkToSource |
                            Plugin.PictureLocations.LinkToOrganisedCopy;

            var pictureUrl = string.Empty;
            var data = Array.Empty<byte>();

            _api.Library_GetArtworkEx(
                trackPath,
                0,
                true,
                out locations,
                out pictureUrl,
                out data
            );

            return data;
        }

        /// <summary>
        ///     Where MusicBee keeps a track's artwork: its location flags and, for a
        ///     linked image, the file path. Asking without the picture data costs
        ///     next to nothing, unlike <see cref="GetArtworkDataForTrack" />.
        /// </summary>
        public Ffi.ArtworkLocation GetArtworkLocationForTrack(string trackPath)
        {
            _api.Library_GetArtworkEx(trackPath, 0, false, out var locations, out var url, out _);
            return new Ffi.ArtworkLocation { location = (int)locations, url = url ?? string.Empty };
        }

        #endregion

        #region Album Cover Cache Support

        /// <summary>
        ///     One identity per album - artist, album, the first track path
        ///     seen, and the newest modification time across its tracks. The
        ///     core fingerprints the library from these, so the Modified value
        ///     is what decides whether the cover cache rebuilds.
        /// </summary>
        public List<(string Artist, string Album, string Path, long Modified)> GetAlbumIdentities()
        {
            // One `Library_QueryFiles` pass folded to one identity per album: a
            // bulk two-tag read and one property read per track. The three-call
            // shape it replaces cost 2-3 full scans plus about 4N tag reads.
            var albums = new Dictionary<string, (string Artist, string Album, string Path, long Modified)>();

            if (!_api.Library_QueryFiles(null))
                return new List<(string, string, string, long)>();

            var fields = new[] { Plugin.MetaDataType.AlbumArtist, Plugin.MetaDataType.Album };

            while (true)
            {
                var track = _api.Library_QueryGetNextFile();
                if (string.IsNullOrEmpty(track))
                    break;

                var success = _api.Library_GetFileTags(track, fields, out var tags);
                var artist = success && tags.Length > 0 ? tags[0].Cleanup() : string.Empty;
                var album = success && tags.Length > 1 ? tags[1].Cleanup() : string.Empty;

                // Fold case-insensitively so two tracks that differ only in tag
                // casing collapse to the one album the core's hashed key would.
                var dedupeKey = artist.ToLowerInvariant() + "\0" + album.ToLowerInvariant();
                var modified = ToUnixSeconds(
                    _api.Library_GetFileProperty(track, Plugin.FilePropertyType.DateModified));

                if (albums.TryGetValue(dedupeKey, out var existing))
                {
                    // Keep the first path seen; carry the newest modification time.
                    if (modified > existing.Modified)
                        albums[dedupeKey] = (existing.Artist, existing.Album, existing.Path, modified);
                }
                else
                {
                    albums[dedupeKey] = (artist, album, track, modified);
                }
            }

            return albums.Values.ToList();
        }

        #endregion

        #region Track Metadata

        public Dictionary<string, (string Artist, string Album)> GetBatchTrackMetadata(IEnumerable<string> trackPaths)
        {
            var result = new Dictionary<string, (string Artist, string Album)>();
            var metaDataFields = new[]
            {
                Plugin.MetaDataType.AlbumArtist,
                Plugin.MetaDataType.Album
            };

            foreach (var trackPath in trackPaths)
            {
                if (string.IsNullOrEmpty(trackPath))
                    continue;

                var tags = new string[metaDataFields.Length];
                var success = _api.Library_GetFileTags(trackPath, metaDataFields, out tags);

                var artist = success && tags.Length > 0 ? tags[0].Cleanup() : string.Empty;
                var album = success && tags.Length > 1 ? tags[1].Cleanup() : string.Empty;

                result[trackPath] = (artist, album);
            }

            return result;
        }

        #endregion

        #region Library Cache

        /// <summary>The names the user gave the given fields, in order.</summary>
        public List<string> GetFieldNames(List<int> fields)
        {
            var names = new List<string>(fields?.Count ?? 0);
            if (fields == null)
                return names;
            foreach (var field in fields)
                names.Add(_api.Setting_GetFieldName((Plugin.MetaDataType)field) ?? string.Empty);
            return names;
        }

        /// <summary>The same fields of each path, read with one call per path.</summary>
        public List<PathTags> GetTagsForPaths(List<int> fields, IEnumerable<string> paths)
        {
            var types = (fields ?? new List<int>()).Select(f => (Plugin.MetaDataType)f).ToArray();
            var result = new List<PathTags>();
            if (paths == null)
                return result;
            foreach (var path in paths)
            {
                var success = _api.Library_GetFileTags(path, types, out var tags);
                var values = new List<string>(types.Length);
                for (var i = 0; i < types.Length; i++)
                    values.Add(success && tags != null && tags.Length > i ? tags[i] ?? string.Empty : string.Empty);
                result.Add(new PathTags { path = path, values = values });
            }
            return result;
        }

        public List<string> GetSourcePaths(LibrarySource source)
        {
            return _api.Library_QueryFilesEx(XmlFilterHelper.AllOf(source), out var files) && files != null
                ? files.ToList()
                : new List<string>();
        }

        /// <summary>
        ///     Every track path in the library, in the same browse order as
        ///     BrowseTracks. The order is the contract, not a side effect: the
        ///     core stores these as an ordinal index and serves browse pages by
        ///     position into it, so returning them in a different order silently
        ///     renumbers every client's track list.
        /// </summary>
        public List<string> GetAllTrackPaths()
        {
            // The array form of the same Library_QueryFiles(null) stream
            // BrowseTracks walks, which is what makes the orders match.
            var success = _api.Library_QueryFilesEx(null, out var files);
            return success && files != null ? files.ToList() : new List<string>();
        }

        public List<Track> GetTracksForPaths(IEnumerable<string> paths)
        {
            var tracks = new List<Track>();
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                _api.Library_GetFileTags(path, BrowseTrackFields, out var tags);

                tracks.Add(new Track
                {
                    artist = Tag(tags, 0).Cleanup(),
                    title = Tag(tags, 1).Cleanup(),
                    album = Tag(tags, 2).Cleanup(),
                    album_artist = Tag(tags, 3).Cleanup(),
                    genre = Tag(tags, 4).Cleanup(),
                    trackno = ParseTag(tags, 5),
                    disc = ParseTag(tags, 6),
                    src = path,
                });
            }

            return tracks;
        }

        public List<TrackTags> GetTrackTags(IEnumerable<string> paths)
        {
            var tracks = new List<TrackTags>();
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                _api.Library_GetFileTags(path, TrackTagFields, out var tags);
                // Duration ("m:ss") and DateAdded (display date) are file properties,
                // not metadata tags.
                var duration = _api.Library_GetFileProperty(path, Plugin.FilePropertyType.Duration);
                var dateAdded = _api.Library_GetFileProperty(path, Plugin.FilePropertyType.DateAdded);

                tracks.Add(new TrackTags
                {
                    src = path,
                    artist = Tag(tags, 0).Cleanup(),
                    title = Tag(tags, 1).Cleanup(),
                    album = Tag(tags, 2).Cleanup(),
                    album_artist = Tag(tags, 3).Cleanup(),
                    genre = Tag(tags, 4).Cleanup(),
                    track_no = ParseTag(tags, 5),
                    disc_no = ParseTag(tags, 6),
                    // Raw values, parsed core-side (year 4-digit extract, rating float).
                    year = Tag(tags, 7),
                    rating = Tag(tags, 8),
                    multi_artist = Tag(tags, 9),
                    multi_composer = Tag(tags, 10),
                    original_year = Tag(tags, 11),
                    // Duration parsed to ms core-side; DateAdded is locale-ambiguous as
                    // a display string, so convert to ISO-8601 UTC here (#114).
                    duration = duration ?? string.Empty,
                    date_added = dateAdded.ToIso8601Utc(),
                });
            }

            return tracks;
        }

        public (List<string> Added, List<string> Updated, List<string> Deleted) GetSyncDelta(long updatedSince)
        {
            var empty = new List<string>();

            // Older MusicBee builds may not bind this delegate; treat as "no delta"
            // so the core falls back to a full index re-fetch.
            if (_api.Library_GetSyncDelta == null)
                return (empty, new List<string>(), new List<string>());

            // MusicBee compares against local file modification times, so feed a
            // local DateTime. Pass no cached files: we only want new + updated here
            // (deletes come from the core's re-fetched path index, so we avoid
            // marshalling the whole library's paths back across the boundary).
            var since = DateTimeOffset.FromUnixTimeSeconds(updatedSince).LocalDateTime;
            var ok = _api.Library_GetSyncDelta(
                Array.Empty<string>(),
                since,
                Plugin.LibraryCategory.Music,
                out var newFiles,
                out var updatedFiles,
                out var deletedFiles);

            if (!ok)
                return (empty, new List<string>(), new List<string>());

            return (
                (newFiles ?? Array.Empty<string>()).ToList(),
                (updatedFiles ?? Array.Empty<string>()).ToList(),
                (deletedFiles ?? Array.Empty<string>()).ToList());
        }

        #endregion

        #region Helper Methods

        /// <summary>Safe indexer into a bulk Library_GetFileTags result (empty when missing).</summary>
        private static string Tag(string[] tags, int index) =>
            tags != null && index < tags.Length && tags[index] != null ? tags[index] : string.Empty;

        /// <summary>A bulk-tag value parsed as an int (0 when missing/unparseable).</summary>
        private static int ParseTag(string[] tags, int index) =>
            int.TryParse(Tag(tags, index), out var value) ? value : 0;

        /// <summary>
        ///     Parse a MusicBee DateModified display string to unix seconds. The
        ///     core compares this against its own "now" (real unix seconds) to
        ///     decide whether a cached cover is stale, so both must be the same
        ///     instant on the same clock. Unparseable => 0 (treated as very old,
        ///     so the cover is kept).
        /// </summary>
        private static long ToUnixSeconds(string dateModified)
        {
            return DateTime.TryParse(dateModified, out var dt)
                ? ((DateTimeOffset)dt).ToUnixTimeSeconds()
                : 0;
        }

        private static AlbumData CreateAlbum(string queryResult)
        {
            var albumInfo = queryResult.Split(NullSeparator);
            albumInfo = albumInfo.Select(s => s.Cleanup()).ToArray();

            if (albumInfo.Length == 1)
                return new AlbumData { artist = albumInfo[0], album = string.Empty, count = 1 };
            if (albumInfo.Length == 2 && queryResult.StartsWith("\0", StringComparison.Ordinal))
                return new AlbumData { artist = albumInfo[1], album = string.Empty, count = 1 };

            return albumInfo.Length == 3
                ? new AlbumData { artist = albumInfo[1], album = albumInfo[2], count = 1 }
                : new AlbumData { artist = albumInfo[0], album = albumInfo[1], count = 1 };
        }

        #endregion
    }
}
