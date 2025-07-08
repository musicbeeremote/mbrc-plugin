using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.Services.Implementations
{
    /// <summary>
    /// Playlist service implementation that delegates to the Plugin instance
    /// </summary>
    public class PlaylistService : IPlaylistService
    {
        public void GetPlaylists(string clientId)
        {
            Plugin.Instance.GetAvailablePlaylistUrls(clientId);
        }

        public void GetPlaylists(string clientId, int offset, int limit)
        {
            Plugin.Instance.GetAvailablePlaylistUrls(clientId, offset, limit);
        }

        public void PlayPlaylist(string clientId, string url)
        {
            Plugin.Instance.PlayPlaylist(clientId, url);
        }
    }
}