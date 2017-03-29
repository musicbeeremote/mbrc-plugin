using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Events.Notifications
{
    public class PlayStateChangedEvent:ITinyMessage
    {
        public object Sender { get; } = null;
    }
}