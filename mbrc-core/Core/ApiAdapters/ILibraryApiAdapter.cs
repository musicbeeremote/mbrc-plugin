using System.Collections.Generic;
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

        string GetNextFile();

        IEnumerable<Genre> GetGenres();

        IEnumerable<Album> GetAlbums();

        IEnumerable<Artist> GetArtists();

        bool LookupGenres();

        bool LookupArtists();

        bool LookupAlbums();

        void CleanLookup();

        string GetYearForTrack(string currentTrack);
    }
}