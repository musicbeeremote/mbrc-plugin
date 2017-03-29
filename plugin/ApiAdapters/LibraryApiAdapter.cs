using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Remote;
using MusicBeeRemoteCore.Remote.Entities;
using MusicBeeRemoteCore.Remote.Model.Entities;
using static MusicBeePlugin.Plugin.MetaDataType;
using Genre = MusicBeeRemoteCore.Remote.Model.Entities.Genre;

namespace MusicBeePlugin.ApiAdapters
{
    class LibraryApiAdapter : ILibraryApiAdapter
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        public LibraryApiAdapter(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        public int GetTrackNumber(string currentTrack)
        {
            int trackNumber;
            int.TryParse(_api.Library_GetFileTag(currentTrack, TrackNo), out trackNumber);
            return trackNumber;
        }

        public int GetDiskNumber(string currentTrack)
        {
            int discNumber;
            int.TryParse(_api.Library_GetFileTag(currentTrack, DiscNo), out discNumber);
            return discNumber;
        }

        public string GetGenreForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Genre).Cleanup();
        }

        public string GetAlbumArtistForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, AlbumArtist).Cleanup();
        }

        public string GetAlbumForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Album).Cleanup();
        }

        public string GetTitleForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, TrackTitle).Cleanup();
        }

        public string GetArtistForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Artist).Cleanup();
        }

        public string[] QueryFiles(string filter = "")
        {
            string[] files = { };
            _api.Library_QueryFilesEx(filter, ref files);
            return files;
        }

        public IEnumerable<Track> GetTracks()
        {
            string[] files = { };
            _api.Library_QueryFilesEx(null, ref files);

            return files.Select(currentTrack => new Track
            {
                Artist = GetArtistForTrack(currentTrack),
                Title = GetTitleForTrack(currentTrack),
                Album = GetAlbumForTrack(currentTrack),
                AlbumArtist = GetAlbumArtistForTrack(currentTrack),
                Genre = GetGenreForTrack(currentTrack),
                Disc = GetDiskNumber(currentTrack),
                Trackno = GetTrackNumber(currentTrack),
                Src = currentTrack
            });
        }

        public IEnumerable<Genre> GetGenres(string filter)
        {
            IEnumerable<Genre> genres = new List<Genre>();

            var query = string.IsNullOrEmpty(filter) ? null : filter;

            if (_api.Library_QueryLookupTable("genre", "count", query))
            {
                genres = _api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split(new[] {'\0'}, StringSplitOptions.None))
                    .Select(genreInfo => new Genre(genreInfo[0].Cleanup(), int.Parse(genreInfo[1])))
                    .ToList();
            }

            _api.Library_QueryLookupTable(null, null, null);

            return genres;
        }

        public IEnumerable<Artist> GetArtists(string filter)
        {
            IEnumerable<Artist> artists = new List<Artist>();

            var query = string.IsNullOrEmpty(filter) ? null : filter;

            if (_api.Library_QueryLookupTable("artist", "count", query))
            {
                artists = _api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split('\0'))
                    .Select(artistInfo => new Artist(artistInfo[0].Cleanup(), int.Parse(artistInfo[1])))
                    .ToList();
            }

            _api.Library_QueryLookupTable(null, null, null);

            return artists;
        }

        public IEnumerable<RadioStation> GetRadioStations()
        {
            var radioStations = new string[] { };
            var success = _api.Library_QueryFilesEx("domain=Radio", ref radioStations);
            List<RadioStation> stations;
            if (success)
            {
                stations = radioStations.Select(s => new RadioStation
                    {
                        Url = s,
                        Name = _api.Library_GetFileTag(s, TrackTitle)
                    })
                    .ToList();
            }
            else
            {
                stations = new List<RadioStation>();
            }
            return stations;
        }

        public IEnumerable<Playlist> GetPlaylists()
        {
            _api.Playlist_QueryPlaylists();
            var playlists = new List<Playlist>();
            while (true)
            {
                var url = _api.Playlist_QueryGetNextPlaylist();

                if (string.IsNullOrEmpty(url))
                {
                    break;
                }

                var name = _api.Playlist_GetName(url);

                var playlist = new Playlist
                {
                    Name = name,
                    Url = url
                };
                playlists.Add(playlist);
            }
            return playlists;
        }

        public IEnumerable<Album> GetAlbums(string filter = "")
        {
            IEnumerable<Album> albums = new List<Album>();

            var query = string.IsNullOrEmpty(filter) ? null : filter;

            if (_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", query))
            {
                albums = _api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s.Trim())
                    .Select(CreateAlbum)
                    .Distinct()
                    .ToList();
            }

            _api.Library_QueryLookupTable(null, null, null);

            return albums;
        }

        public string GetYearForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, Year);
        }

        public bool PlayPlaylist(string url)
        {
            return _api.Playlist_PlayNow(url);
        }

        private static Album CreateAlbum(string queryResult)
        {
            var albumInfo = queryResult.Split('\0');

            albumInfo = albumInfo.Select(s => s.Cleanup()).ToArray();

            if (albumInfo.Length == 1)
            {
                return new Album(albumInfo[0], string.Empty);
            }
            if (albumInfo.Length == 2 && queryResult.StartsWith("\0"))
            {
                return new Album(albumInfo[1], string.Empty);
            }

            var current = albumInfo.Length == 3
                ? new Album(albumInfo[1], albumInfo[2])
                : new Album(albumInfo[0], albumInfo[1]);

            return current;
        }
    }
}