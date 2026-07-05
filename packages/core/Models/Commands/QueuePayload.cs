using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Commands
{
    [DataContract]
    public class QueuePayload
    {
        [DataMember(Name = "queue")] public string Queue { get; set; }

        [DataMember(Name = "data")] public List<string> Data { get; set; }
    }
}
