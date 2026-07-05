using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Requests
{
    /// <summary>
    ///     Request payload for album cover retrieval
    /// </summary>
    [DataContract]
    public class AlbumCoverRequest : PaginationRequest
    {
        [DataMember(Name = "album")] public string Album { get; set; }

        [DataMember(Name = "artist")] public string Artist { get; set; }

        [DataMember(Name = "hash")] public string Hash { get; set; }

        [DataMember(Name = "size")] public string Size { get; set; }

        /// <summary>
        ///     Returns true if this is a paginated cover request
        /// </summary>
        public bool IsPaginatedRequest => Limit > 0 && Album == null && Artist == null;
    }
}
