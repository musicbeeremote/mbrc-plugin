using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Models;
using MusicBeePlugin.Ffi;
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

        // Tag sets read in one bulk Library_GetFileTags call per track (instead of
        // one MusicBee API round-trip per tag). Index order is consumed below.
        private static readonly Plugin.MetaDataType[] BrowseTrackFields =
        {
            Plugin.MetaDataType.Artist, Plugin.MetaDataType.TrackTitle, Plugin.MetaDataType.Album,
            Plugin.MetaDataType.AlbumArtist, Plugin.MetaDataType.Genre,
            Plugin.MetaDataType.TrackNo, Plugin.MetaDataType.DiscNo,
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

        public List<AlbumData> BrowseAlbums(int offset = 0, int limit = 4000)
        {
            // Returns List because deduplication requires collapsing duplicates.
            // The internal AlbumData used IEquatable + Distinct (dedup by
            // artist+album, count stays 1); replicate that externally with a
            // first-wins HashSet keyed on artist\0album so ordering and count are
            // identical (do NOT increment - BrowseAlbums count is always 1).
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

        public IEnumerable<Track> GetAlbumTracks(string album, SearchSource searchSource)
        {
            var filter = XmlFilterHelper.CreateFilter(
                AlbumSearchFields, album, true, searchSource);

            if (!_api.Library_QueryFiles(filter))
                yield break;

            while (true)
            {
                var currentTrack = _api.Library_QueryGetNextFile();
                if (string.IsNullOrEmpty(currentTrack))
                    break;

                _api.Library_GetFileTags(currentTrack, AlbumTrackFields, out var tags);

                // album/genre are intentionally left empty here (the old
                // ToTrackDto null-coalesced the unset internal fields to empty).
                yield return new Track
                {
                    artist = Tag(tags, 0),
                    title = Tag(tags, 1),
                    album_artist = Tag(tags, 2),
                    trackno = ParseTag(tags, 3),
                    disc = ParseTag(tags, 4),
                    src = currentTrack,
                    album = string.Empty,
                    genre = string.Empty
                };
            }
        }

        public List<AlbumData> GetArtistAlbums(string artist, SearchSource searchSource)
        {
            // Dedup by (albumArtist, album) and count the tracks per album. The
            // retired internal AlbumData did this via IEquatable + IncreaseCount;
            // here a Dictionary keyed on albumArtist\0album carries the count.
            var albums = new Dictionary<string, AlbumData>(StringComparer.Ordinal);
            var filter = XmlFilterHelper.CreateFilter(
                ArtistSearchFields, artist, true, searchSource);

            if (_api.Library_QueryFiles(filter))
                while (true)
                {
                    var currentFile = _api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentFile))
                        break;

                    _api.Library_GetFileTags(currentFile, ArtistAlbumFields, out var tags);
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

        public IEnumerable<ArtistData> GetGenreArtists(string genre, SearchSource searchSource)
        {
            var filter = XmlFilterHelper.CreateFilter(
                GenreSearchFields, genre, true, searchSource);

            if (!_api.Library_QueryLookupTable("artist", "count", filter))
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
                            artist = artistInfo[0] ?? string.Empty,
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

        #endregion

        #region Album Cover Cache Support

        public List<(string Artist, string Album, string Path, long Modified)> GetAlbumIdentities()
        {
            // One library scan, folded to one identity per album. The shipped
            // PrepareCache did this in three calls (GetAllAlbumIdentifiers +
            // GetTrackPaths + GetFileModificationDates), each re-enumerating the
            // library and re-reading tags - 2-3 full scans plus ~4N tag reads.
            // Here it is a single Library_QueryFiles pass: a bulk two-tag read and
            // one property read per track, folded in memory.
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

        #region Library Cache (MBRCIP-0001)

        public List<string> GetAllTrackPaths()
        {
            // The array form of the Library_QueryFiles(null) stream BrowseTracks
            // uses, so the paths come back in the same browse order - the core's
            // ordinal index relies on that.
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
