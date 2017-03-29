using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Events.Notifications
{
    public class ArtworkReadyEvent: ITinyMessage
    {
        public object Sender { get; } = null;
    }
}