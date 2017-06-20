using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model
{
    [DataContract]
    public class IdentifiableRequest
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }
    }
}