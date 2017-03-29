using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Events.Notifications
{
    public class VolumeMuteChangedEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}