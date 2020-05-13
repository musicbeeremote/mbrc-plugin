using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Status.Internal
{
    public class ConnectionReadyEvent : ITinyMessage
    {
        public ConnectionReadyEvent(SocketConnection client)
        {
            Client = client;
        }

        public object Sender { get; } = null;

        public SocketConnection Client { get; }
    }
}
