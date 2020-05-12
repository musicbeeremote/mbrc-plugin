using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class StartServiceBroadcastEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}
