using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.Services.Implementations
{
    /// <summary>
    /// Library service implementation that delegates to the Plugin instance
    /// </summary>
    public class LibraryService : ILibraryService
    {
        public void SearchAlbums(string albumName, string clientId)
        {
            Plugin.Instance.LibrarySearchAlbums(albumName, clientId);
        }

        public void SearchArtist(string artist, string clientId)
        {
            Plugin.Instance.LibrarySearchArtist(artist, clientId);
        }

        public void SearchGenres(string genre, string clientId)
        {
            Plugin.Instance.LibrarySearchGenres(genre, clientId);
        }

        public void SearchTitle(string title, string clientId)
        {
            Plugin.Instance.LibrarySearchTitle(title, clientId);
        }

        public void BrowseGenres(string clientId, int offset = 0, int limit = 4000)
        {
            Plugin.Instance.LibraryBrowseGenres(clientId, offset, limit);
        }

        public void BrowseArtists(string clientId, int offset = 0, int limit = 4000, bool albumArtists = false)
        {
            Plugin.Instance.LibraryBrowseArtists(clientId, offset, limit, albumArtists);
        }

        public void BrowseAlbums(string clientId, int offset = 0, int limit = 4000)
        {
            Plugin.Instance.LibraryBrowseAlbums(clientId, offset, limit);
        }

        public void BrowseTracks(string clientId, int offset = 0, int limit = 4000)
        {
            Plugin.Instance.LibraryBrowseTracks(clientId, offset, limit);
        }

        public void GetArtistAlbums(string artist, string clientId)
        {
            Plugin.Instance.LibraryGetArtistAlbums(artist, clientId);
        }

        public void GetGenreArtists(string genre, string clientId)
        {
            Plugin.Instance.LibraryGetGenreArtists(genre, clientId);
        }

        public void GetAlbumTracks(string album, string client)
        {
            Plugin.Instance.LibraryGetAlbumTracks(album, client);
        }

        public void GetRadioStations(string clientId, int offset = 0, int limit = 4000)
        {
            Plugin.Instance.RequestRadioStations(clientId, offset, limit);
        }

        public void QueueFiles(QueueType queue, MetaTag tag, string query)
        {
            Plugin.Instance.RequestQueueFiles(queue, tag, query);
        }

        public bool QueueFiles(QueueType queue, string[] data, string query = "")
        {
            return Plugin.Instance.QueueFiles(queue, data, query);
        }

        public string[] GetUrlsForTag(MetaTag tag, string query)
        {
            return Plugin.Instance.GetUrlsForTag(tag, query);
        }

        public void PlayAll(string clientId, bool shuffle = false)
        {
            Plugin.Instance.LibraryPlayAll(clientId, shuffle);
        }

        public void GetCover(string clientId, string artist, string album, string clientHash, string size)
        {
            Plugin.Instance.RequestCover(clientId, artist, album, clientHash, size);
        }

        public void GetCoverPage(string clientId, int offset, int limit)
        {
            Plugin.Instance.RequestCoverPage(clientId, offset, limit);
        }
    }
}