using MusicBeeRemoteCore.Remote.Enumerations;

namespace MusicBeeRemoteCore.Core.Support
{
    public interface ISearchQueue
    {
        /// <summary>
        /// Implements the legacy (before version 1.0.0) queuing mechanism that used search in
        /// and keywords in order to supply queuing functionality
        /// </summary>
        /// <param name="queue">The actual queue action (can be next, last, now)</param>
        /// <param name="tag">The type of tag that will be queued</param>
        /// <param name="query">The tag value that will be queued, or the track if it is a single track</param>
        void RequestQueueFiles(QueueType queue, MetaTag tag, string query);
    }
}