using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Events.Notifications
{
    public class NowPlayingListChanged: ITinyMessage
    {
        public object Sender { get; } = null;
    }
}