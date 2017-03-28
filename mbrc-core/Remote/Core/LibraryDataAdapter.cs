using System.Collections.Generic;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Remote.Model.Entities;

namespace MusicBeeRemoteCore.Remote.Core
{
    internal class LibraryDataAdapter : ILibraryDataAdapter
    {
        private readonly ILibraryApiAdapter _libraryApi;

        public LibraryDataAdapter(ILibraryApiAdapter libraryApi)
        {
            _libraryApi = libraryApi;
        }

        public List<Genre> GetGenres()
        {
            throw new System.NotImplementedException();
        }

        public List<Artist> GetArtists()
        {
            throw new System.NotImplementedException();
        }

        public List<Album> GetAlbums()
        {
            throw new System.NotImplementedException();
        }

        public List<Track> GetTracks()
        {
            var tracks = new List<Track>();

            if (!_libraryApi.QueryFiles()) return tracks;

            while (true)
            {
                var currentTrack = _libraryApi.GetNextFile();

                if (string.IsNullOrEmpty(currentTrack)) break;

                var track = new Track
                {
                    Artist = _libraryApi.GetArtistForTrack(currentTrack),
                    Title = _libraryApi.GetTitleForTrack(currentTrack),
                    Album = _libraryApi.GetAlbumForTrack(currentTrack),
                    AlbumArtist = _libraryApi.GetAlbumArtistForTrack(currentTrack),
                    Genre = _libraryApi.GetGenreForTrack(currentTrack),
                    Disc = _libraryApi.GetDiskNumber(currentTrack),
                    Trackno = _libraryApi.GetTrackNumber(currentTrack),
                    Src = currentTrack
                };
                tracks.Add(track);
            }
            return tracks;
        }
    }
}