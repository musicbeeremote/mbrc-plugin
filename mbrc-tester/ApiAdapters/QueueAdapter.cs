using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;

namespace MbrcTester.ApiAdapters
{
    public class QueueAdapter : IQueueAdapter
    {
        public bool QueueFiles(QueueType queue, string[] data, string query = "")
        {
            switch (queue)
            {
                case QueueType.Next:
                    throw new System.NotImplementedException();
                case QueueType.Last:
                    throw new System.NotImplementedException();
                case QueueType.PlayNow:
                    throw new System.NotImplementedException();
                case QueueType.AddAndPlay:
                    throw new System.NotImplementedException();
                default:
                    return false;
            }
        }
    }
}
