using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class ClientDisconnectedEvent : ITinyMessage
    {
        public ClientDisconnectedEvent(string connectionId)
        {
            ConnectionId = connectionId;
        }

        public string ConnectionId { get; }

        public object Sender { get; } = null;
    }
}
