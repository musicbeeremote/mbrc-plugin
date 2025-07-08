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

        public void LibrarySearchTitle(string query, string clientId)
        {
            Plugin.Instance.LibrarySearchTitle(query, clientId);
        }

        public void LibrarySearchArtist(string query, string clientId)
        {
            Plugin.Instance.LibrarySearchArtist(query, clientId);
        }

        public void LibrarySearchAlbums(string query, string clientId)
        {
            Plugin.Instance.LibrarySearchAlbums(query, clientId);
        }

        public void LibrarySearchGenres(string query, string clientId)
        {
            Plugin.Instance.LibrarySearchGenres(query, clientId);
        }

        public void LibraryGetGenreArtists(string genre, string clientId)
        {
            Plugin.Instance.LibraryGetGenreArtists(genre, clientId);
        }

        public void LibraryGetArtistAlbums(string artist, string clientId)
        {
            Plugin.Instance.LibraryGetArtistAlbums(artist, clientId);
        }

        public void LibraryGetAlbumTracks(string album, string clientId)
        {
            Plugin.Instance.LibraryGetAlbumTracks(album, clientId);
        }

        public void LibraryPlayAll(string clientId, bool shuffle)
        {
            Plugin.Instance.LibraryPlayAll(clientId, shuffle);
        }

        public void RequestQueueFiles(QueueType queueType, MetaTag tag, string query)
        {
            Plugin.Instance.RequestQueueFiles(queueType, tag, query);
        }

        public void RequestCover(string clientId, string artist, string album, string hash, string size)
        {
            Plugin.Instance.RequestCover(clientId, artist, album, hash, size);
        }

        public void RequestCoverPage(string clientId, int offset, int limit)
        {
            Plugin.Instance.RequestCoverPage(clientId, offset, limit);
        }

        public void RequestRadioStations(string clientId, int offset = 0, int limit = 4000)
        {
            Plugin.Instance.RequestRadioStations(clientId, offset, limit);
        }

        public void RequestRadioStations(string clientId)
        {
            Plugin.Instance.RequestRadioStations(clientId);
        }
    }
}