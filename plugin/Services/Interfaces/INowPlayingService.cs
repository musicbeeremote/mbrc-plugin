using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Model.Entities;

namespace MusicBeePlugin.Services.Interfaces
{
    /// <summary>
    /// Service interface for now playing and queue operations
    /// </summary>
    public interface INowPlayingService
    {
        /// <summary>
        /// Gets current track information
        /// </summary>
        void GetTrackInfo(string clientId);
        
        /// <summary>
        /// Gets detailed track information
        /// </summary>
        void GetTrackDetails(string clientId);
        
        /// <summary>
        /// Gets playing track details
        /// </summary>
        NowPlayingDetails GetPlayingTrackDetails();
        
        /// <summary>
        /// Sets rating for current track
        /// </summary>
        void SetTrackRating(string rating, string clientId);
        
        /// <summary>
        /// Sets tag value for current track
        /// </summary>
        void SetTrackTag(string tagName, string value, string clientId);
        
        /// <summary>
        /// Sets love status for current track
        /// </summary>
        void SetLoveStatus(string action, string clientId);
        
        /// <summary>
        /// Gets lyrics for current track
        /// </summary>
        void GetTrackLyrics();
        
        /// <summary>
        /// Gets cover art for current track
        /// </summary>
        void GetTrackCover();
        
        /// <summary>
        /// Gets the now playing list
        /// </summary>
        void GetNowPlayingList(string clientId);
        
        /// <summary>
        /// Gets a page of the now playing list
        /// </summary>
        void GetNowPlayingListPage(string clientId, int offset = 0, int limit = 4000);
        
        /// <summary>
        /// Gets ordered now playing list
        /// </summary>
        void GetNowPlayingListOrdered(string clientId, int offset = 0, int limit = 100);
        
        /// <summary>
        /// Plays a specific track from the now playing list
        /// </summary>
        void PlayFromNowPlayingList(string index, bool isAndroid);
        
        /// <summary>
        /// Removes a track from the now playing list
        /// </summary>
        void RemoveFromNowPlayingList(int index, string clientId);
        
        /// <summary>
        /// Moves a track in the now playing list
        /// </summary>
        void MoveInNowPlayingList(string clientId, int from, int to);
        
        /// <summary>
        /// Searches the now playing list
        /// </summary>
        void SearchNowPlayingList(string query, string clientId);
        
        /// <summary>
        /// Additional now playing methods
        /// </summary>
        void RequestNowPlayingMove(string clientId, int from, int to);
        void NowPlayingPlay(string index, bool isAndroid);
        bool QueueFiles(QueueType queue, string[] data, string play);
        void NowPlayingSearch(string query, string clientId);
        void NowPlayingListRemoveTrack(int index, string clientId);
        void RequestNowPlayingTrackCover();
        void RequestNowPlayingTrackLyrics();
        void RequestTrackInfo(string clientId);
        void RequestTrackRating(string rating, string clientId);
    }
}