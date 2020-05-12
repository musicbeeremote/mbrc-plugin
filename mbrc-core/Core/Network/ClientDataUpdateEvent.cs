using TinyMessenger;

namespace MusicBeeRemote.Core.Network
{
    public class ClientDataUpdateEvent : ITinyMessage
    {
        public ClientDataUpdateEvent(RemoteClient client)
        {
            Client = client;
        }

        public RemoteClient Client { get; }

        public object Sender { get; } = null;
    }
}
