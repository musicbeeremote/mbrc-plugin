using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.Services.Implementations
{
    /// <summary>
    /// Now playing service implementation that delegates to the Plugin instance
    /// </summary>
    public class NowPlayingService : INowPlayingService
    {
        public void GetTrackInfo(string clientId)
        {
            Plugin.Instance.RequestTrackInfo(clientId);
        }

        public void GetTrackDetails(string clientId)
        {
            Plugin.Instance.RequestTrackDetails(clientId);
        }

        public NowPlayingDetails GetPlayingTrackDetails()
        {
            return Plugin.Instance.GetPlayingTrackDetails();
        }

        public void SetTrackRating(string rating, string clientId)
        {
            Plugin.Instance.RequestTrackRating(rating, clientId);
        }

        public void SetTrackTag(string tagName, string value, string clientId)
        {
            Plugin.Instance.SetTrackTag(tagName, value, clientId);
        }

        public void SetLoveStatus(string action, string clientId)
        {
            Plugin.Instance.RequestLoveStatus(action, clientId);
        }

        public void GetTrackLyrics()
        {
            Plugin.Instance.RequestNowPlayingTrackLyrics();
        }

        public void GetTrackCover()
        {
            Plugin.Instance.RequestNowPlayingTrackCover();
        }

        public void GetNowPlayingList(string clientId)
        {
            Plugin.Instance.RequestNowPlayingList(clientId);
        }

        public void GetNowPlayingListPage(string clientId, int offset = 0, int limit = 4000)
        {
            Plugin.Instance.RequestNowPlayingListPage(clientId, offset, limit);
        }

        public void GetNowPlayingListOrdered(string clientId, int offset = 0, int limit = 100)
        {
            Plugin.Instance.RequestNowPlayingListOrdered(clientId, offset, limit);
        }

        public void PlayFromNowPlayingList(string index, bool isAndroid)
        {
            Plugin.Instance.NowPlayingPlay(index, isAndroid);
        }

        public void RemoveFromNowPlayingList(int index, string clientId)
        {
            Plugin.Instance.NowPlayingListRemoveTrack(index, clientId);
        }

        public void MoveInNowPlayingList(string clientId, int from, int to)
        {
            Plugin.Instance.RequestNowPlayingMove(clientId, from, to);
        }

        public void SearchNowPlayingList(string query, string clientId)
        {
            Plugin.Instance.NowPlayingSearch(query, clientId);
        }

        public void RequestNowPlayingMove(string clientId, int from, int to)
        {
            Plugin.Instance.RequestNowPlayingMove(clientId, from, to);
        }

        public void NowPlayingPlay(string index, bool isAndroid)
        {
            Plugin.Instance.NowPlayingPlay(index, isAndroid);
        }

        public bool QueueFiles(QueueType queue, string[] data, string play)
        {
            return Plugin.Instance.QueueFiles(queue, data, play);
        }

        public void NowPlayingSearch(string query, string clientId)
        {
            Plugin.Instance.NowPlayingSearch(query, clientId);
        }

        public void NowPlayingListRemoveTrack(int index, string clientId)
        {
            Plugin.Instance.NowPlayingListRemoveTrack(index, clientId);
        }

        public void RequestNowPlayingTrackCover()
        {
            Plugin.Instance.RequestNowPlayingTrackCover();
        }

        public void RequestNowPlayingTrackLyrics()
        {
            Plugin.Instance.RequestNowPlayingTrackLyrics();
        }

        public void RequestTrackInfo(string clientId)
        {
            Plugin.Instance.RequestTrackInfo(clientId);
        }

        public void RequestTrackRating(string rating, string clientId)
        {
            Plugin.Instance.RequestTrackRating(rating, clientId);
        }
    }
}