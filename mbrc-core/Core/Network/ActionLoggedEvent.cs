using MusicBeeRemote.Core.Commands.Logs;
using TinyMessenger;

namespace MusicBeeRemote.Core.Network
{
    public class ActionLoggedEvent : ITinyMessage
    {
        public ActionLoggedEvent(ExecutionLog log)
        {
            Log = log;
        }

        public object Sender { get; } = null;

        public ExecutionLog Log { get; }
    }
}
