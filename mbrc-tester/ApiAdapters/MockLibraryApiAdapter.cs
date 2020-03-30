using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Caching.Monitor;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Podcasts;
using Genre = MusicBeeRemote.Core.Model.Entities.Genre;

namespace MbrcTester.ApiAdapters
{
    class LibraryApiAdapter : ILibraryApiAdapter
    {
        private readonly MockLibrary _mockLibrary;

        public LibraryApiAdapter(MockLibrary mockLibrary)
        {
            _mockLibrary = mockLibrary;
        }

        public IEnumerable<Track> GetTracks(string[] paths)
        {
            return _mockLibrary.GetTracks();
        }

        public Modifications GetSyncDelta(string[] cachedFiles, DateTime lastSync)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Genre> GetGenres(string filter)
        {
            return from track in _mockLibrary.GetTracks()
                where track.Genre.ToLowerInvariant().Contains(filter.ToLowerInvariant())
                orderby track.Genre
                group track by track.Genre
                into grp
                select new Genre(grp.Key, grp.Count());
        }

        public IEnumerable<Artist> GetArtists(string filter)
        {
            return from track in _mockLibrary.GetTracks()
                where track.Artist.ToLowerInvariant().Contains(filter.ToLowerInvariant())
                orderby track.Artist
                group track by track.Artist
                into grp
                select new Artist(grp.Key, grp.Count());
        }

        public IEnumerable<RadioStation> GetRadioStations()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<PodcastSubscription> GetPodcastSubscriptions()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<PodcastEpisode> GetEpisodes(string subscriptionId)
        {
            throw new NotImplementedException();
        }

        public byte[] GetPodcastSubscriptionArtwork(string subscriptionId)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Playlist> GetPlaylists()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Album> GetAlbums(string filter = "")
        {
            return from track in _mockLibrary.GetTracks()
                where track.Album != null && track.Album.ToLowerInvariant().Contains(filter.ToLowerInvariant())
                orderby track.Album
                group track by track.Album
                into grp
                select new Album(grp.Key, grp.Any() ? grp.First().AlbumArtist : "");
        }

        public bool PlayPlaylist(string url)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetTrackPaths()
        {
            throw new NotImplementedException();
        }
    }
}