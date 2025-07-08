using MusicBeePlugin.AndroidRemote.Enumerations;

namespace MusicBeePlugin.Services.Interfaces
{
    /// <summary>
    /// Service interface for library browsing and searching operations
    /// </summary>
    public interface ILibraryService
    {
        /// <summary>
        /// Searches for albums by name
        /// </summary>
        void SearchAlbums(string albumName, string clientId);
        
        /// <summary>
        /// Searches for artists by name
        /// </summary>
        void SearchArtist(string artist, string clientId);
        
        /// <summary>
        /// Searches for genres by name
        /// </summary>
        void SearchGenres(string genre, string clientId);
        
        /// <summary>
        /// Searches for tracks by title
        /// </summary>
        void SearchTitle(string title, string clientId);
        
        /// <summary>
        /// Browses all genres with pagination
        /// </summary>
        void BrowseGenres(string clientId, int offset = 0, int limit = 4000);
        
        /// <summary>
        /// Browses all artists with pagination
        /// </summary>
        void BrowseArtists(string clientId, int offset = 0, int limit = 4000, bool albumArtists = false);
        
        /// <summary>
        /// Browses all albums with pagination
        /// </summary>
        void BrowseAlbums(string clientId, int offset = 0, int limit = 4000);
        
        /// <summary>
        /// Browses all tracks with pagination
        /// </summary>
        void BrowseTracks(string clientId, int offset = 0, int limit = 4000);
        
        /// <summary>
        /// Gets albums for a specific artist
        /// </summary>
        void GetArtistAlbums(string artist, string clientId);
        
        /// <summary>
        /// Gets artists for a specific genre
        /// </summary>
        void GetGenreArtists(string genre, string clientId);
        
        /// <summary>
        /// Gets tracks for a specific album
        /// </summary>
        void GetAlbumTracks(string album, string client);
        
        /// <summary>
        /// Gets available radio stations
        /// </summary>
        void GetRadioStations(string clientId, int offset = 0, int limit = 4000);
        
        /// <summary>
        /// Queues files by tag and query
        /// </summary>
        void QueueFiles(QueueType queue, MetaTag tag, string query);
        
        /// <summary>
        /// Queues specific files
        /// </summary>
        bool QueueFiles(QueueType queue, string[] data, string query = "");
        
        /// <summary>
        /// Gets URLs for specific tag and query
        /// </summary>
        string[] GetUrlsForTag(MetaTag tag, string query);
        
        /// <summary>
        /// Plays all tracks in library
        /// </summary>
        void PlayAll(string clientId, bool shuffle = false);
        
        /// <summary>
        /// Gets cover art for artist/album
        /// </summary>
        void GetCover(string clientId, string artist, string album, string clientHash, string size);
        
        /// <summary>
        /// Gets a page of covers
        /// </summary>
        void GetCoverPage(string clientId, int offset, int limit);
        
        /// <summary>
        /// Library search methods
        /// </summary>
        void LibrarySearchTitle(string query, string clientId);
        void LibrarySearchArtist(string query, string clientId);
        void LibrarySearchAlbums(string query, string clientId);
        void LibrarySearchGenres(string query, string clientId);
        
        /// <summary>
        /// Library browse methods
        /// </summary>
        void LibraryGetGenreArtists(string genre, string clientId);
        void LibraryGetArtistAlbums(string artist, string clientId);
        void LibraryGetAlbumTracks(string album, string clientId);
        
        /// <summary>
        /// Library actions
        /// </summary>
        void LibraryPlayAll(string clientId, bool shuffle);
        void RequestQueueFiles(QueueType queueType, MetaTag tag, string query);
        void RequestCover(string clientId, string artist, string album, string hash, string size);
        void RequestCoverPage(string clientId, int offset, int limit);
        void RequestRadioStations(string clientId, int offset = 0, int limit = 4000);
        void RequestRadioStations(string clientId);
    }
}