using System;

namespace MusicBeeRemote.PartyMode.Core.Model
{
    public class PartyModeLogs
    {
        public DateTime TimeStamp { get; }
        
        public PartyModeLogs(string client, string command, ExecutionStatus	 status)
        {
            TimeStamp = DateTime.Now;
            Client = Client == string.Empty ? "---" : client;
            Command = command;
            Status = status;
        }

        public string Client { get; }
        public string Command { get; }
        public ExecutionStatus Status { get; }
    }
}