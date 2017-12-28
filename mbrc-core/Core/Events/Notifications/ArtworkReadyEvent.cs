using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Notifications
{
    public class ArtworkReadyEvent: ITinyMessage
    {
        public object Sender { get; } = null;
    }
}