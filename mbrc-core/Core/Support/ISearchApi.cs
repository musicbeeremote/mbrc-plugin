using System.Collections.Generic;
using System.Linq;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Remote.Model.Entities;
using static MusicBeeRemoteCore.Core.Support.FilterHelper;

namespace MusicBeeRemoteCore.Core.Support
{
    public interface ISearchApi
    {
        List<Track> LibrarySearchTitle(string title);
        List<Track> LibraryGetAlbumTracks(string album);
        List<Genre> LibrarySearchGenres(string genre);
        List<Album> LibrarySearchAlbums(string albumName);
        List<Album> LibraryGetArtistAlbums(string artist);
        List<Artist> LibrarySearchArtist(string artist);
        List<Artist> LibraryGetGenreArtists(string genre);
    }

    class SearchApi : ISearchApi
    {
        private readonly ILibraryApiAdapter _apiAdapter;

        public SearchApi(ILibraryApiAdapter apiAdapter)
        {
            _apiAdapter = apiAdapter;
        }

        public List<Track> LibrarySearchTitle(string title)
        {
            var tracks = _apiAdapter.QueryFiles(XmlFilter(new[] {"Title"}, title, false));
            return tracks.ToList().Select(file =>
            {
                var artist = _apiAdapter.GetArtistForTrack(file);
                var trackTitle = _apiAdapter.GetTitleForTrack(file);
                var trackNumber = _apiAdapter.GetTrackNumber(file);
                var track = new Track(artist, trackTitle, trackNumber, file);
                return track;
            }).ToList();
        }

        public List<Track> LibraryGetAlbumTracks(string album)
        {
            throw new System.NotImplementedException();
        }

        public List<Genre> LibrarySearchGenres(string genre)
        {
            throw new System.NotImplementedException();
        }

        public List<Album> LibrarySearchAlbums(string albumName)
        {
            throw new System.NotImplementedException();
        }

        public List<Album> LibraryGetArtistAlbums(string artist)
        {
            throw new System.NotImplementedException();
        }

        public List<Artist> LibrarySearchArtist(string artist)
        {
            throw new System.NotImplementedException();
        }

        public List<Artist> LibraryGetGenreArtists(string genre)
        {
            throw new System.NotImplementedException();
        }
    }
}