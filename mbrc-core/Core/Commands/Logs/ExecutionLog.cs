using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Commands.Logs
{
    [DataContract]
    public class ExecutionLog
    {
        [DisplayName("Date")]
        [DataMember(Name = "date")]
        public DateTime TimeStamp { get; set; } = DateTime.Now;

        [DisplayName("Client")]
        [DataMember(Name = "client")]
        public string Client { get; set; }

        [DisplayName("Command")]
        [DataMember(Name = "command")]
        public string Command { get; set; }

        [DisplayName("Status")]
        [DataMember(Name = "status")]
        public ExecutionStatus Status { get; set; } = ExecutionStatus.Executed;
    }
}
