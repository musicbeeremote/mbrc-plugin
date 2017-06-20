using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Network.Http
{
    [DataContract]
    public class Response
    {
        [DataMember(Name = "code")]
        public int Code { get; set; }
        
        [DataMember(Name = "description")]
        public string Description { get; set; }
    }
}