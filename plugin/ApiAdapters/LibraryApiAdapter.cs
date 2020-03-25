using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Caching.Monitor;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Podcasts;
using MusicBeeRemote.Core.Utilities;
using static MusicBeePlugin.Plugin.MetaDataType;
using Genre = MusicBeeRemote.Core.Model.Entities.Genre;

namespace MusicBeePlugin.ApiAdapters
{
    class LibraryApiAdapter : ILibraryApiAdapter
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        public LibraryApiAdapter(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        private int GetTrackNumber(string currentTrack)
        {
            int.TryParse(_api.Library_GetFileTag(currentTrack, TrackNo), out var trackNumber);
            return trackNumber;
        }

        private int GetDiskNumber(string currentTrack)
        {
            int.TryParse(_api.Library_GetFileTag(currentTrack, DiscNo), out var discNumber);
            return discNumber;
        }

        private string GetGenreForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Genre).Cleanup();
        }

        private string GetAlbumArtistForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, AlbumArtist).Cleanup();
        }

        private string GetAlbumForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Album).Cleanup();
        }

        private string GetTitleForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, TrackTitle).Cleanup();
        }

        private string GetArtistForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Artist).Cleanup();
        }

        private string GetAlbumYear(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, Year).Cleanup();
        }

        public IEnumerable<Track> GetTracks(string[] paths)
        {
            string[] files;
            if (paths == null)
            {
                _api.Library_QueryFilesEx(null, out files);
            }
            else
            {
                files = paths;
            }
            
            return files.Select(currentTrack => new Track
            {
                Artist = GetArtistForTrack(currentTrack),
                Title = GetTitleForTrack(currentTrack),
                Album = GetAlbumForTrack(currentTrack),
                AlbumArtist = GetAlbumArtistForTrack(currentTrack),
                Year = GetAlbumYear(currentTrack),
                Genre = GetGenreForTrack(currentTrack),
                Disc = GetDiskNumber(currentTrack),
                Trackno = GetTrackNumber(currentTrack),
                Src = currentTrack
            }).ToList();
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
            var success = _api.Library_QueryFilesEx("domain=Radio", out var radioStations);
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

        public IEnumerable<PodcastSubscription> GetPodcastSubscriptions()
        {
            var list = new List<PodcastSubscription>();
            _api.Podcasts_QuerySubscriptions(null, out var subscriptionIds);
            var subscriptionConverter = new SubscriptionConverter();
                   
            foreach (var id in subscriptionIds)
            {
                if (_api.Podcasts_GetSubscription(id, out var subscriptionMetadata))
                {
                    list.Add(subscriptionConverter.Convert(subscriptionMetadata));
                }                
            }
            
            return list;
        }

        public IEnumerable<PodcastEpisode> GetEpisodes(string subscriptionId)
        {
            string[] subscriptionIds;
            _api.Podcasts_QuerySubscriptions(null, out subscriptionIds);

            var converter = new EpisodeConverter();
            var list = new List<PodcastEpisode>();            
            var episodeIndex = 0;

            if (!_api.Podcasts_GetSubscriptionEpisodes(subscriptionId, out var episodes))
            {
                return list;
            }

            for (var i = 0; i < episodes.Length; i++)
            {
                if (!_api.Podcasts_GetSubscriptionEpisode(subscriptionId, i, out var episodeMetadata))
                {
                    break;
                }
                list.Add(converter.Convert(episodeMetadata));
            }
            
            return list;
        }

        public byte[] GetPodcastSubscriptionArtwork(string subscriptionId)
        {
            return _api.Podcasts_GetSubscriptionArtwork(subscriptionId, 0, out var artwork) 
                ? Utilities.ToJpeg(artwork) 
                : new byte[] {};
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

        public bool PlayPlaylist(string url)
        {
            return _api.Playlist_PlayNow(url);
        }
        
        public Modifications GetSyncDelta(string[] cachedFiles, DateTime lastSync)
        {
            _api.Library_GetSyncDelta(
                cachedFiles,
                lastSync,
                Plugin.LibraryCategory.Music,
                out var newFiles,
                out var updatedFiles,
                out var deletedFiles);
            return new Modifications(deletedFiles, newFiles, updatedFiles);
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