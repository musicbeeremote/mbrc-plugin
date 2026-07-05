using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Utilities.Common;
using MusicBeePlugin.Utilities.Data;

namespace MusicBeePlugin.DataProviders
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
        private static readonly string[] TitleFilter = { "Title" };

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
                    Url = s,
                    Name = _api.Library_GetFileTag(s, Plugin.MetaDataType.TrackTitle)
                }).ToList();
            else
                stations = new List<RadioStation>();

            return stations;
        }

        #endregion

        #region Search Operations

        public IEnumerable<GenreData> SearchGenres(string query, SearchSource searchSource)
        {
            var filter = XmlFilterHelper.CreateFilter(
                GenreSearchFields,
                query,
                false,
                searchSource);

            if (!_api.Library_QueryLookupTable("genre", "count", filter))
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
                        yield return new GenreData(
                            genreInfo[0],
                            int.Parse(genreInfo[1], CultureInfo.InvariantCulture));
                    }
                }
            }
            finally
            {
                _api.Library_QueryLookupTable(null, null, null);
            }
        }

        public IEnumerable<ArtistData> SearchArtists(string query, SearchSource searchSource)
        {
            var filter = XmlFilterHelper.CreateFilter(
                ArtistSearchFields,
                query,
                false,
                searchSource);

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
                        yield return new ArtistData(
                            artistInfo[0],
                            int.Parse(artistInfo[1], CultureInfo.InvariantCulture));
                    }
                }
            }
            finally
            {
                _api.Library_QueryLookupTable(null, null, null);
            }
        }

        public List<AlbumData> SearchAlbums(string query, SearchSource searchSource)
        {
            // Returns List because deduplication requires Contains/IndexOf operations
            var filter = XmlFilterHelper.CreateFilter(
                AlbumSearchFields,
                query,
                false,
                searchSource);

            var albums = new List<AlbumData>();

            if (_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", filter))
                try
                {
                    foreach (var entry in new List<string>(_api.Library_QueryGetLookupTableValue(null)
                                 .Split(DoubleSeparator, StringSplitOptions.None)))
                    {
                        if (string.IsNullOrEmpty(entry))
                            continue;
                        var albumInfo = entry.Split(NullSeparator);
                        if (albumInfo.Length < 2)
                            continue;

                        var current = albumInfo.Length == 3
                            ? new AlbumData(albumInfo[1], albumInfo[2])
                            : new AlbumData(albumInfo[0], albumInfo[1]);

                        if (current.Album.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        if (!albums.Contains(current))
                            albums.Add(current);
                        else
                            albums.ElementAt(albums.IndexOf(current)).IncreaseCount();
                    }
                }
                catch (IndexOutOfRangeException)
                {
                    // Intentionally ignored: Skip malformed album data entries
                }

            _api.Library_QueryLookupTable(null, null, null);
            return albums;
        }

        public IEnumerable<Track> SearchTracks(string query, SearchSource searchSource)
        {
            var filter = XmlFilterHelper.CreateFilter(
                TitleFilter,
                query,
                false,
                searchSource);

            if (!_api.Library_QueryFiles(filter))
            {
                _api.Library_QueryLookupTable(null, null, null);
                yield break;
            }

            try
            {
                while (true)
                {
                    var currentTrack = _api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack))
                        break;

                    var trackNumber = int.TryParse(
                        _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.TrackNo),
                        out var parsedTrackNumber)
                        ? parsedTrackNumber
                        : 0;

                    yield return new Track(
                        _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Artist),
                        _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.TrackTitle),
                        trackNumber,
                        currentTrack);
                }
            }
            finally
            {
                _api.Library_QueryLookupTable(null, null, null);
            }
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
                        yield return new GenreData(
                            genreInfo[0].Cleanup(),
                            int.Parse(genreInfo[1], CultureInfo.InvariantCulture));
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
                        yield return new ArtistData(
                            artistInfo[0].Cleanup(),
                            int.Parse(artistInfo[1], CultureInfo.InvariantCulture));
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
            // Returns List because deduplication requires Distinct operation
            var albums = new List<AlbumData>();

            if (_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", null))
                try
                {
                    var data = _api.Library_QueryGetLookupTableValue(null)
                        .Split(DoubleSeparator, StringSplitOptions.None)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Select(s => s.Trim())
                        .Select(CreateAlbum)
                        .Distinct()
                        .ToList();

                    albums.AddRange(data);
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

                var trackNumber = int.TryParse(
                    _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.TrackNo),
                    out var parsedTrackNumber)
                    ? parsedTrackNumber
                    : 0;
                var discNumber = int.TryParse(
                    _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.DiscNo),
                    out var parsedDiscNumber)
                    ? parsedDiscNumber
                    : 0;

                yield return new Track
                {
                    Artist = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Artist).Cleanup(),
                    Title = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.TrackTitle).Cleanup(),
                    Album = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Album).Cleanup(),
                    AlbumArtist = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.AlbumArtist).Cleanup(),
                    Genre = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Genre).Cleanup(),
                    Disc = discNumber,
                    TrackNo = trackNumber,
                    Src = currentTrack
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

                var trackNumber = int.TryParse(
                    _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.TrackNo),
                    out var parsedTrackNumber)
                    ? parsedTrackNumber
                    : 0;
                var discNumber = int.TryParse(
                    _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.DiscNo),
                    out var parsedDiscNumber)
                    ? parsedDiscNumber
                    : 0;

                yield return new Track
                {
                    Artist = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Artist),
                    Title = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.TrackTitle),
                    AlbumArtist = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.AlbumArtist),
                    Disc = discNumber,
                    TrackNo = trackNumber,
                    Src = currentTrack
                };
            }
        }

        public List<AlbumData> GetArtistAlbums(string artist, SearchSource searchSource)
        {
            // Returns List because deduplication requires Contains/IndexOf operations
            var albumList = new List<AlbumData>();
            var filter = XmlFilterHelper.CreateFilter(
                ArtistSearchFields, artist, true, searchSource);

            if (_api.Library_QueryFiles(filter))
                while (true)
                {
                    var currentFile = _api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentFile))
                        break;

                    var current = new AlbumData(
                        _api.Library_GetFileTag(currentFile, Plugin.MetaDataType.AlbumArtist),
                        _api.Library_GetFileTag(currentFile, Plugin.MetaDataType.Album));

                    if (!albumList.Contains(current))
                        albumList.Add(current);
                    else
                        albumList.ElementAt(albumList.IndexOf(current)).IncreaseCount();
                }

            return albumList;
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
                        yield return new ArtistData(
                            artistInfo[0],
                            int.Parse(artistInfo[1], CultureInfo.InvariantCulture));
                    }
                }
            }
            finally
            {
                _api.Library_QueryLookupTable(null, null, null);
            }
        }

        #endregion

        #region Queue Support

        public string[] GetFileUrlsByMetaTag(MetaTag tag, string query, SearchSource searchSource)
        {
            var filter = string.Empty;
            string[] tracks = Array.Empty<string>();

            switch (tag)
            {
                case MetaTag.Artist:
                    filter = XmlFilterHelper.CreateFilter(
                        ArtistSearchFields, query, true, searchSource);
                    break;
                case MetaTag.Album:
                    filter = XmlFilterHelper.CreateFilter(
                        AlbumSearchFields, query, true, searchSource);
                    break;
                case MetaTag.Genre:
                    filter = XmlFilterHelper.CreateFilter(
                        GenreSearchFields, query, true, searchSource);
                    break;
                case MetaTag.Title:
                    filter = "";
                    break;
            }

            _api.Library_QueryFilesEx(filter, out tracks);

            var metaDataFields = new[]
            {
                Plugin.MetaDataType.Artist,
                Plugin.MetaDataType.AlbumArtist,
                Plugin.MetaDataType.Album,
                Plugin.MetaDataType.TrackTitle,
                Plugin.MetaDataType.Genre,
                Plugin.MetaDataType.Year,
                Plugin.MetaDataType.TrackNo,
                Plugin.MetaDataType.DiscNo
            };

            var list = tracks.Select(file =>
            {
                var tags = new string[metaDataFields.Length];
                var success = _api.Library_GetFileTags(file, metaDataFields, out tags);
                return new MetaData
                {
                    File = file,
                    Artist = success && tags.Length > 0 ? tags[0] : string.Empty,
                    AlbumArtist = success && tags.Length > 1 ? tags[1] : string.Empty,
                    Album = success && tags.Length > 2 ? tags[2] : string.Empty,
                    Title = success && tags.Length > 3 ? tags[3] : string.Empty,
                    Genre = success && tags.Length > 4 ? tags[4] : string.Empty,
                    Year = success && tags.Length > 5 ? tags[5] : string.Empty,
                    TrackNo = success && tags.Length > 6 ? tags[6] : string.Empty,
                    Disc = success && tags.Length > 7 ? tags[7] : string.Empty
                };
            }).ToList();

            list.Sort();
            return list.Select(r => r.File).ToArray();
        }

        #endregion

        #region Artwork

        public string GetArtworkForTrack(string trackPath)
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

            return pictureUrl;
        }

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

        public Dictionary<string, string> GetAllAlbumIdentifiers()
        {
            var identifiers = new Dictionary<string, string>();
            if (!_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", null))
                return identifiers;

            try
            {
                var data = _api.Library_QueryGetLookupTableValue(null)
                    .Split(DoubleSeparator, StringSplitOptions.None)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s.Trim())
                    .Select(CreateAlbum)
                    .ToList();

                foreach (var album in data)
                {
                    var key = Utilities.Common.Utilities.CoverIdentifier(album.Artist, album.Album);
                    if (!identifiers.ContainsKey(key))
                        identifiers[key] = $"{album.Artist}|{album.Album}";
                }
            }
            catch (IndexOutOfRangeException)
            {
                // Handle silently like in original code
            }

            _api.Library_QueryLookupTable(null, null, null);
            return identifiers;
        }

        public Dictionary<string, string> GetTrackPaths()
        {
            var paths = new Dictionary<string, string>();
            var identifiers = GetAllAlbumIdentifiers();

            if (!_api.Library_QueryFiles(null))
                return paths;

            while (true)
            {
                var currentTrack = _api.Library_QueryGetNextFile();
                if (string.IsNullOrEmpty(currentTrack))
                    break;

                var album = GetAlbumForTrack(currentTrack);
                var artist = GetAlbumArtistForTrack(currentTrack);

                try
                {
                    var key = Utilities.Common.Utilities.CoverIdentifier(artist, album);

                    if (!identifiers.ContainsKey(key))
                        continue;

                    paths[key] = currentTrack;
                }
                catch (Exception)
                {
                    // Handle silently like in original code
                }
            }

            return paths;
        }

        public Dictionary<string, string> GetFileModificationDates()
        {
            var modified = new Dictionary<string, string>();
            var identifiers = GetAllAlbumIdentifiers();

            if (!_api.Library_QueryFiles(null))
                return modified;

            while (true)
            {
                var currentTrack = _api.Library_QueryGetNextFile();
                if (string.IsNullOrEmpty(currentTrack))
                    break;

                var album = GetAlbumForTrack(currentTrack);
                var artist = GetAlbumArtistForTrack(currentTrack);
                var fileModified = _api.Library_GetFileProperty(currentTrack, Plugin.FilePropertyType.DateModified);

                try
                {
                    var key = Utilities.Common.Utilities.CoverIdentifier(artist, album);

                    if (!identifiers.ContainsKey(key))
                        continue;

                    modified[key] = fileModified;
                }
                catch (Exception)
                {
                    // Handle silently like in original code
                }
            }

            return modified;
        }

        #endregion

        #region Track Metadata

        public string GetAlbumArtistForTrack(string trackPath)
        {
            return _api.Library_GetFileTag(trackPath, Plugin.MetaDataType.AlbumArtist).Cleanup();
        }

        public string GetAlbumForTrack(string trackPath)
        {
            return _api.Library_GetFileTag(trackPath, Plugin.MetaDataType.Album).Cleanup();
        }

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

        #region Helper Methods

        private static AlbumData CreateAlbum(string queryResult)
        {
            var albumInfo = queryResult.Split(NullSeparator);
            albumInfo = albumInfo.Select(s => s.Cleanup()).ToArray();

            if (albumInfo.Length == 1)
                return new AlbumData(albumInfo[0], string.Empty);
            if (albumInfo.Length == 2 && queryResult.StartsWith("\0", StringComparison.Ordinal))
                return new AlbumData(albumInfo[1], string.Empty);

            var current = albumInfo.Length == 3
                ? new AlbumData(albumInfo[1], albumInfo[2])
                : new AlbumData(albumInfo[0], albumInfo[1]);

            return current;
        }

        #endregion
    }
}
