using System.Collections.Generic;
using MusicBeeRemoteCore.Remote.Model.Entities;

namespace MusicBeeRemoteCore.Remote.Core
{
    internal interface ILibraryApiAdapter
    {
        List<Genre> GetGenres();
        List<Artist> GetArtists();
        List<Album> GetAlbums();
        List<Track> GetTracks();
    }

    internal class LibraryApiAdapter : ILibraryApiAdapter
    {
        private readonly IApiAdapter _api;

        public LibraryApiAdapter(IApiAdapter api)
        {
            _api = api;
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

            if (!_api.QueryFiles()) return tracks;

            while (true)
            {
                var currentTrack = _api.GetNextFile();

                if (string.IsNullOrEmpty(currentTrack)) break;

                var track = new Track
                {
                    Artist = _api.GetArtistForTrack(currentTrack),
                    Title = _api.GetTitleForTrack(currentTrack),
                    Album = _api.GetAlbumForTrack(currentTrack),
                    AlbumArtist = _api.GetAlbumArtistForTrack(currentTrack),
                    Genre = _api.GetGenreForTrack(currentTrack),
                    Disc = _api.GetDiskNumber(currentTrack),
                    Trackno = _api.GetTrackNumber(currentTrack),
                    Src = currentTrack
                };
                tracks.Add(track);
            }
            return tracks;
        }
    }
}