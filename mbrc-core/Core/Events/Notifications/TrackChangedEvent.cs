using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Events.Notifications
{
    public class TrackChangedEvent: ITinyMessage
    {
        public object Sender { get; } = null;
    }
}