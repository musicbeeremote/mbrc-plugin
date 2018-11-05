using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model
{
    [DataContract]
    public class IdentifiablePaginatedRequest : PaginatedRequest
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }
    }
}