using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model
{
    [DataContract]
    public class PaginatedRequest
    {
        [DataMember(Name = "offset")]
        public int Offset { get; set; }

        [DataMember(Name = "limit")]
        public int Limit { get; set; } = 500;
    }
}