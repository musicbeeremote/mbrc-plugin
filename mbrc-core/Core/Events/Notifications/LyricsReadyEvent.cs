using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Notifications
{
    public class LyricsReadyEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}