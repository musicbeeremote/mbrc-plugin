using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Notifications
{
    public class VolumeLevelChangedEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}