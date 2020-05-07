using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    /// <inheritdoc />
    public class QueueAdapter : IQueueAdapter
    {
        private readonly MusicBeeApiInterface _api;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueueAdapter"/> class.
        /// </summary>
        /// <param name="api">The MusicBee API.</param>
        public QueueAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <inheritdoc/>
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
