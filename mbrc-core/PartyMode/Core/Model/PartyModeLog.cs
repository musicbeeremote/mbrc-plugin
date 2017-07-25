using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace MusicBeeRemote.PartyMode.Core.Model
{
    [DataContract]
    public class PartyModeLog
    {            
        [DisplayName("Date")]
        [DataMember(Name = "date")]
        public DateTime TimeStamp { get; set; }
        
        [DisplayName("Client")]
        [DataMember(Name = "client")]
        public string Client { get; set; }
        
        [DisplayName("Command")]
        [DataMember(Name = "command")]
        public string Command { get; set; }
        
        [DisplayName("Status")]
        [DataMember(Name = "status")]
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