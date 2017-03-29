using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeeRemoteCore.Remote.Commands
{
    [DataContract]
    public class QueuePayload
    {
        [DataMember(Name = "queue")]
        public string Queue { get; set; }
        [DataMember(Name = "data")]
        public List<string> Data { get; set; }
    }
}