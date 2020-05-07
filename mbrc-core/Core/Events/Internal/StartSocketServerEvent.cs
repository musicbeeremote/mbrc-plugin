using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class StartSocketServerEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}
