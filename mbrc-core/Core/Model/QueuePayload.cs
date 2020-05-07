using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model
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
