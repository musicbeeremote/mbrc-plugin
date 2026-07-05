using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Responses
{
    [DataContract]
    public class AlbumCoverPayload
    {
        [DataMember(Name = "album")] public string Album { get; set; }
        [DataMember(Name = "artist")] public string Artist { get; set; }
        [DataMember(Name = "cover")] public string Cover { get; set; }
        [DataMember(Name = "status")] public int Status { get; set; }
        [DataMember(Name = "hash")] public string Hash { get; set; }
    }
}
