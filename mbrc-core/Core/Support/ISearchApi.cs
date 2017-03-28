using System.Collections.Generic;
using MusicBeeRemoteCore.Remote.Model.Entities;

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
}