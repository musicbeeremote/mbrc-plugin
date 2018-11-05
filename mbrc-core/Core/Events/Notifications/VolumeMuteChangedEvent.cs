using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Notifications
{
    public class VolumeMuteChangedEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}