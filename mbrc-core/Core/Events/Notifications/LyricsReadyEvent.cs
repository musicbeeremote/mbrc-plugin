using TinyMessenger;

namespace MusicBeeRemoteCore.Core.Events.Notifications
{
    public class LyricsReadyEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}