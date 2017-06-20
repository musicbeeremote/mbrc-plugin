using System.Collections.Generic;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Podcasts;

namespace MusicBeeRemote.Core.ApiAdapters
{
    public interface ILibraryApiAdapter
    {
        int GetTrackNumber(string currentTrack);

        int GetDiskNumber(string currentTrack);

        string GetGenreForTrack(string currentTrack);

        string GetAlbumArtistForTrack(string currentTrack);

        string GetAlbumForTrack(string currentTrack);

        string GetTitleForTrack(string currentTrack);

        string GetArtistForTrack(string currentTrack);

        string[] QueryFiles(string filter = "");

        IEnumerable<Track> GetTracks();

        IEnumerable<Genre> GetGenres(string filter = "");

        IEnumerable<Album> GetAlbums(string filter = "");

        IEnumerable<Artist> GetArtists(string filter = "");

        IEnumerable<RadioStation> GetRadioStations();

        IEnumerable<PodcastSubscription> GetPodcastSubscriptions();

        IEnumerable<PodcastEpisode> GetEpisodes(string subscriptionId);

        byte[] GetPodcastSubscriptionArtwork(string subscriptionId);
        
        IEnumerable<Playlist> GetPlaylists();

        string GetYearForTrack(string currentTrack);

        bool PlayPlaylist(string url);
    }
}