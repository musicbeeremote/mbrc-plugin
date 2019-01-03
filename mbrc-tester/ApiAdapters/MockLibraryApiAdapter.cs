using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Podcasts;
using MusicBeeRemote.Core.Utilities;
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

        public IEnumerable<Track> GetTracks()
        {
            return _mockLibrary.GetTracks();
        }

        public IEnumerable<Genre> GetGenres(string filter)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<Artist> GetArtists(string filter)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<RadioStation> GetRadioStations()
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<PodcastSubscription> GetPodcastSubscriptions()
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<PodcastEpisode> GetEpisodes(string subscriptionId)
        {
            throw new System.NotImplementedException();
        }

        public byte[] GetPodcastSubscriptionArtwork(string subscriptionId)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<Playlist> GetPlaylists()
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<Album> GetAlbums(string filter = "")
        {
            throw new System.NotImplementedException();
        }

        public bool PlayPlaylist(string url)
        {
            throw new System.NotImplementedException();
        }
    }
}