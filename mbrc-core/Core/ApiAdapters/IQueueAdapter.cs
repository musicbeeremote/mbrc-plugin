using MusicBeeRemote.Core.Enumerations;

namespace MusicBeeRemote.Core.ApiAdapters
{
    public interface IQueueAdapter
    {
        bool QueueFiles(QueueType queue, string[] data, string query = "");
    }
}