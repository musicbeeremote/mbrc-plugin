using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Requests
{
    /// <summary>
    ///     Base request payload for paginated requests
    /// </summary>
    [DataContract]
    public class PaginationRequest
    {
        /// <summary>
        ///     Default limit for paginated requests
        /// </summary>
        public const int DefaultLimit = 4000;

        [DataMember(Name = "offset")] public int Offset { get; set; }

        [DataMember(Name = "limit")] public int Limit { get; set; } = DefaultLimit;
    }
}
