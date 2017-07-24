using TinyMessenger;

namespace MusicBeeRemote.PartyMode.Core.Model
{
    public class CommandProcessedEvent : ITinyMessage
    {
        public PartyModeLog Log { get; }
        
        public object Sender { get; } = null;
        
        public CommandProcessedEvent(PartyModeLog log)
        {
            Log = log;
        }
    }
}