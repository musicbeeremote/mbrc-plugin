using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Events.Notifications
{
    public class VolumeLevelChangedEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}