using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Requests
{
    /// <summary>
    ///     Request payload for library search
    /// </summary>
    [DataContract]
    public class SearchRequest
    {
        [DataMember(Name = "type")] public string Type { get; set; }

        [DataMember(Name = "query")] public string Query { get; set; }
    }
}
