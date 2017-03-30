using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Events.Notifications
{
    public class NowPlayingListChangedEvent: ITinyMessage
    {
        public object Sender { get; } = null;
    }
}