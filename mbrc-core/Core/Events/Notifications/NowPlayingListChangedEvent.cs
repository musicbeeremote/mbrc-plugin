using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Notifications
{
    public class NowPlayingListChangedEvent: ITinyMessage
    {
        public object Sender { get; } = null;
    }
}