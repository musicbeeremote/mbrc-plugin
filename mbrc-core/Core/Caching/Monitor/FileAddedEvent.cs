using TinyMessenger;

namespace MusicBeeRemote.Core.Caching.Monitor
{
    public class FileAddedEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}