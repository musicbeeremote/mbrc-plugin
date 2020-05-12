using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Status.Internal
{
    internal class ConnectionRemovedEvent : ITinyMessage
    {
        public ConnectionRemovedEvent(SocketConnection client)
        {
            Client = client;
        }

        public SocketConnection Client { get; }

        public object Sender { get; } = null;
    }
}
