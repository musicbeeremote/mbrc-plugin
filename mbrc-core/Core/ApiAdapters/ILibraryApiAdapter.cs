using System.Collections.Generic;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Podcasts;

namespace MusicBeeRemote.Core.ApiAdapters
{
    public interface ILibraryApiAdapter
    {        
        IEnumerable<Track> GetTracks();

        IEnumerable<Genre> GetGenres(string filter = "");

        IEnumerable<Album> GetAlbums(string filter = "");

        IEnumerable<Artist> GetArtists(string filter = "");

        IEnumerable<RadioStation> GetRadioStations();

        IEnumerable<PodcastSubscription> GetPodcastSubscriptions();

        IEnumerable<PodcastEpisode> GetEpisodes(string subscriptionId);

        byte[] GetPodcastSubscriptionArtwork(string subscriptionId);
        
        IEnumerable<Playlist> GetPlaylists();

        bool PlayPlaylist(string url);
    }
}