using System;

namespace MusicBeeRemote.PartyMode.Core.Model
{
    public class PartyModeLog
    {    
        public DateTime TimeStamp { get; set; }
        public string Client { get; set; }
        public string Command { get; set; }
        public ExecutionStatus Status { get; set; }

        public PartyModeLog()
        {
        }

        public PartyModeLog(string client, string command, ExecutionStatus status)
        {
            TimeStamp = DateTime.Now;
            Client = Client == string.Empty ? "---" : client;
            Command = command;
            Status = status;
        }
    }
}