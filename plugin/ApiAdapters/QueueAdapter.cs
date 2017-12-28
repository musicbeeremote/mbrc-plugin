using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    public class QueueAdapter : IQueueAdapter
    {
        private readonly MusicBeeApiInterface _api;

        public QueueAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        public bool QueueFiles(QueueType queue, string[] data, string query = "")
        {
            switch (queue)
            {
                case QueueType.Next:
                    return _api.NowPlayingList_QueueFilesNext(data);
                case QueueType.Last:
                    return _api.NowPlayingList_QueueFilesLast(data);
                case QueueType.PlayNow:
                    _api.NowPlayingList_Clear();
                    _api.NowPlayingList_QueueFilesLast(data);
                    return _api.NowPlayingList_PlayNow(data[0]);
                case QueueType.AddAndPlay:
                    _api.NowPlayingList_Clear();
                    _api.NowPlayingList_QueueFilesLast(data);
                    return _api.NowPlayingList_PlayNow(query);
                default:
                    return false;
            }
        }
    }
}