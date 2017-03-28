using System.Collections.Generic;
using MusicBeeRemoteCore.Remote.Model.Entities;

namespace MusicBeeRemoteCore.Remote.Core
{
    internal interface ILibraryDataAdapter
    {
        List<Genre> GetGenres();
        List<Artist> GetArtists();
        List<Album> GetAlbums();
        List<Track> GetTracks();
    }
}