using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class RestartSocketEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}
