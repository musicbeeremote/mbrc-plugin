using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class ConnectionReadyEvent : ITinyMessage
    {
        public ConnectionReadyEvent(SocketConnection client)
        {
            Client = client;
        }

        public object Sender { get; } = null;

        public SocketConnection Client { get; }
    }
}
