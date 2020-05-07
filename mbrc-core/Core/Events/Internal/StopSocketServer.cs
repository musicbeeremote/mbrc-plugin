using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class StopSocketServer : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}
