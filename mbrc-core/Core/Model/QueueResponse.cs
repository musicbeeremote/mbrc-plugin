using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model
{
    [DataContract]
    public class QueueResponse
    {
        [DataMember(Name = "code")]
        public int Code { get; set; }
    }
}
