using MusicBeeRemote.Core.Commands.Logs;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Events
{
    public class CommandProcessedEvent : ITinyMessage
    {
        public ExecutionLog Log { get; }
        
        public object Sender { get; } = null;
        
        public CommandProcessedEvent(ExecutionLog log)
        {
            Log = log;
        }
    }
}