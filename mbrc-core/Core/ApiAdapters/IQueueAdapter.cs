using MusicBeeRemoteCore.Remote.Enumerations;

namespace MusicBeeRemoteCore.Core.ApiAdapters
{
    public interface IQueueAdapter
    {
        bool QueueFiles(QueueType queue, string[] data, string query = "");
    }
}