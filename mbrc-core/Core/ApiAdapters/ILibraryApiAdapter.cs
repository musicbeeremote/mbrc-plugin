using System.Collections.Generic;
using MusicBeeRemoteCore.Remote.Entities;
using MusicBeeRemoteCore.Remote.Model.Entities;

namespace MusicBeeRemoteCore.Core.ApiAdapters
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

        IEnumerable<Playlist> GetPlaylists();

        string GetYearForTrack(string currentTrack);

        bool PlayPlaylist(string url);
    }
}