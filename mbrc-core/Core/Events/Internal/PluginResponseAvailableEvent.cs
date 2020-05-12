using MusicBeeRemote.Core.Model.Entities;
using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    public class PluginResponseAvailableEvent : ITinyMessage
    {
        public PluginResponseAvailableEvent(SocketMessage message, string connectionId = "all")
        {
            Message = message;
            ConnectionId = connectionId;
        }

        public object Sender { get; } = null;

        public SocketMessage Message { get; }

        public string ConnectionId { get; }
    }
}
