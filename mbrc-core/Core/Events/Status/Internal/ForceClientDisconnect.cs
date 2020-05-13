using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Status.Internal
{
    public class ForceClientDisconnect : ITinyMessage
    {
        public ForceClientDisconnect(string connectionId)
        {
            ConnectionId = connectionId;
        }

        public object Sender { get; } = null;

        public string ConnectionId { get; }
    }
}
