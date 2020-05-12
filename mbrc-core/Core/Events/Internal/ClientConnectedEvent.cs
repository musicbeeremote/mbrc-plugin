using System.Net;
using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class ClientConnectedEvent : ITinyMessage
    {
        public ClientConnectedEvent(IPAddress ipAddress, string connectionId)
        {
            IpAddress = ipAddress;
            ConnectionId = connectionId;
        }

        public object Sender { get; } = null;

        public IPAddress IpAddress { get; }

        public string ConnectionId { get; }
    }
}
