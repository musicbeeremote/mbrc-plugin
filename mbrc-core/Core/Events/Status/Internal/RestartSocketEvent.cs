using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Status.Internal
{
    public class RestartSocketEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}
