using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Notifications
{
    public class PlayStateChangedEvent:ITinyMessage
    {
        public object Sender { get; } = null;
    }
}