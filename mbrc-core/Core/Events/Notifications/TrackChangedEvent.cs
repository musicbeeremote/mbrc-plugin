using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Notifications
{
    public class TrackChangedEvent: ITinyMessage
    {
        public object Sender { get; } = null;
    }
}