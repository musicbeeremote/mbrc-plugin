namespace MusicBeePlugin.Services.Interfaces
{
    /// <summary>
    /// Service interface for playlist operations
    /// </summary>
    public interface IPlaylistService
    {
        /// <summary>
        /// Gets all available playlists
        /// </summary>
        void GetPlaylists(string clientId);
        
        /// <summary>
        /// Gets available playlists with pagination
        /// </summary>
        void GetPlaylists(string clientId, int offset, int limit);
        
        /// <summary>
        /// Plays a specific playlist
        /// </summary>
        void PlayPlaylist(string clientId, string url);
        
        /// <summary>
        /// Gets available playlist URLs
        /// </summary>
        void GetAvailablePlaylistUrls(string clientId);
        void GetAvailablePlaylistUrls(string clientId, int offset, int limit);
    }
}