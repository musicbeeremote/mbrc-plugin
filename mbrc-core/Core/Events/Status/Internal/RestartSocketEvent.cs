using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Status.Internal
{
    internal class RestartSocketEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}
