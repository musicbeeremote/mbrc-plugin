using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class SocketStatusChanged : ITinyMessage
    {
        // todo (window info should listen for this
        public SocketStatusChanged(bool isRunning)
        {
            IsRunning = isRunning;
        }

        public object Sender { get; } = null;

        public bool IsRunning { get; }
    }
}
