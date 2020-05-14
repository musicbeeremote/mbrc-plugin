using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <inheritdoc/>
    public class LibraryApiAdapter : ILibraryApiAdapter
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryApiAdapter"/> class.
        /// </summary>
        /// <param name="api">The MusicBee API.</param>
        public LibraryApiAdapter(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <inheritdoc/>
        public IEnumerable<Track> GetTracks(string[] paths = null)
        {
            string[] files;
            if (paths == null)
            {
                _api.Library_QueryFilesEx(null, out files);
            }
            else
            {
                if (paths.Length == 0)
                {
                    return new List<Track>();
                }

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
                Src = currentTrack,
            }).ToList();
        }

        /// <inheritdoc/>
        public IEnumerable<Genre> GetGenres(string filter = "")
        {
            IEnumerable<Genre> genres = new List<Genre>();

            var query = string.IsNullOrEmpty(filter) ? null : filter;

            if (_api.Library_QueryLookupTable("genre", "count", query))
            {
                genres = _api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] { "\0\0" }, StringSplitOptions.None)
                    .Select(entry => entry.Split(new[] { '\0' }, StringSplitOptions.None))
                    .Select(genreInfo => new Genre(genreInfo[0].Cleanup(), int.Parse(genreInfo[1], CultureInfo.CurrentCulture)))
                    .ToList();
            }

            _api.Library_QueryLookupTable(null, null, null);

            return genres;
        }

        /// <inheritdoc/>
        public IEnumerable<Artist> GetArtists(string filter = "")
        {
            IEnumerable<Artist> artists = new List<Artist>();

            var query = string.IsNullOrEmpty(filter) ? null : filter;

            if (_api.Library_QueryLookupTable("artist", "count", query))
            {
                artists = _api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] { "\0\0" }, StringSplitOptions.None)
                    .Select(entry => entry.Split('\0'))
                    .Select(artistInfo => new Artist(artistInfo[0].Cleanup(), int.Parse(artistInfo[1], CultureInfo.CurrentCulture)))
                    .ToList();
            }

            _api.Library_QueryLookupTable(null, null, null);

            return artists;
        }

        /// <inheritdoc/>
        public IEnumerable<RadioStation> GetRadioStations()
        {
            var success = _api.Library_QueryFilesEx("domain=Radio", out var radioStations);
            List<RadioStation> stations;
            if (success)
            {
                stations = radioStations.Select(s => new RadioStation
                {
                    Url = s,
                    Name = _api.Library_GetFileTag(s, TrackTitle),
                }).ToList();
            }
            else
            {
                stations = new List<RadioStation>();
            }

            return stations;
        }

        /// <inheritdoc/>
        public IEnumerable<PodcastSubscription> GetPodcastSubscriptions()
        {
            var list = new List<PodcastSubscription>();
            _api.Podcasts_QuerySubscriptions(null, out var subscriptionIds);

            foreach (var id in subscriptionIds)
            {
                if (_api.Podcasts_GetSubscription(id, out var subscriptionMetadata))
                {
                    list.Add(SubscriptionConverter.Convert(subscriptionMetadata));
                }
            }

            return list;
        }

        /// <inheritdoc/>
        public IEnumerable<PodcastEpisode> GetEpisodes(string subscriptionId)
        {
            var list = new List<PodcastEpisode>();
            _api.Podcasts_QuerySubscriptions(null, out _);

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

                list.Add(EpisodeConverter.Convert(episodeMetadata));
            }

            return list;
        }

        /// <inheritdoc/>
        public byte[] GetPodcastSubscriptionArtwork(string subscriptionId)
        {
            return _api.Podcasts_GetSubscriptionArtwork(subscriptionId, 0, out var artwork)
                ? ArtworkUtilities.ToJpeg(artwork)
                : new byte[] { };
        }

        /// <inheritdoc/>
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
                    Url = url,
                };
                playlists.Add(playlist);
            }

            return playlists;
        }

        /// <inheritdoc/>
        public IEnumerable<Album> GetAlbums(string filter = "")
        {
            IEnumerable<Album> albums = new List<Album>();

            var query = string.IsNullOrEmpty(filter) ? null : filter;

            if (_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", query))
            {
                albums = _api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] { "\0\0" }, StringSplitOptions.None)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s.Trim())
                    .Select(CreateAlbum)
                    .Distinct()
                    .ToList();
            }

            _api.Library_QueryLookupTable(null, null, null);

            return albums;
        }

        /// <inheritdoc/>
        public bool PlayPlaylist(string path)
        {
            return _api.Playlist_PlayNow(path);
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetTrackPaths()
        {
            _api.Library_QueryFilesEx(null, out var files);
            return files;
        }

        /// <inheritdoc/>
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

            switch (albumInfo.Length)
            {
                case 1:
                    return new Album(albumInfo[0], string.Empty);
                case 2 when queryResult.StartsWith("\0", StringComparison.InvariantCultureIgnoreCase):
                    return new Album(albumInfo[1], string.Empty);
                default:
                    {
                        var current = albumInfo.Length == 3
                            ? new Album(albumInfo[1], albumInfo[2])
                            : new Album(albumInfo[0], albumInfo[1]);

                        return current;
                    }
            }
        }

        private int GetTrackNumber(string currentTrack)
        {
            if (int.TryParse(_api.Library_GetFileTag(currentTrack, TrackNo), out var trackNumber))
            {
                return trackNumber;
            }

            return -1;
        }

        private int GetDiskNumber(string currentTrack)
        {
            if (int.TryParse(_api.Library_GetFileTag(currentTrack, DiscNo), out var discNumber))
            {
                return discNumber;
            }

            return -1;
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
    }
}
