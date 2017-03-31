using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model.Entities;

namespace MusicBeeRemote.Core.Support
{
    class SearchApi : ISearchApi
    {
        private readonly ILibraryApiAdapter _apiAdapter;

        public SearchApi(ILibraryApiAdapter apiAdapter)
        {
            _apiAdapter = apiAdapter;
        }

        public List<Track> LibrarySearchTitle(string title)
        {
            var tracks = _apiAdapter.QueryFiles(FilterHelper.XmlFilter(new[] {"Title"}, title, false));
            return tracks.ToList().Select(CreateTrackForPath).ToList();
        }

        private Track CreateTrackForPath(string file)
        {
            var artist = _apiAdapter.GetArtistForTrack(file);
            var trackTitle = _apiAdapter.GetTitleForTrack(file);
            var trackNumber = _apiAdapter.GetTrackNumber(file);
            var track = new Track(artist, trackTitle, trackNumber, file);
            return track;
        }

        public List<Track> LibraryGetAlbumTracks(string album)
        {
            var paths = _apiAdapter.QueryFiles(FilterHelper.XmlFilter(new[] {"Album"}, album, true));
            var tracks = paths.ToList().Select(CreateTrackForPath).ToList();
            tracks.Sort();
            return tracks;
        }

        public List<Genre> LibrarySearchGenres(string genre)
        {
            return _apiAdapter.GetGenres(FilterHelper.XmlFilter(new[] {"Genre"}, genre, false)).ToList();
        }

        public List<Album> LibrarySearchAlbums(string albumName)
        {
            return _apiAdapter.GetAlbums(FilterHelper.XmlFilter(new[] {"Album"}, albumName, false)).ToList();
        }

        public List<Album> LibraryGetArtistAlbums(string artist)
        {
            return _apiAdapter.GetAlbums(FilterHelper.XmlFilter(new[] {"ArtistPeople"}, artist, true)).ToList();
        }

        public List<Artist> LibrarySearchArtist(string artist)
        {
            return _apiAdapter.GetArtists(FilterHelper.XmlFilter(new[] {"ArtistPeople"}, artist, false)).ToList();
        }

        public List<Artist> LibraryGetGenreArtists(string genre)
        {
            return _apiAdapter.GetArtists(FilterHelper.XmlFilter(new[] {"Genre"}, genre, true)).ToList();
        }
    }
}