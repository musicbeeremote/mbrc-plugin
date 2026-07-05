using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Requests
{
    /// <summary>
    ///     Request payload for browsing artists with optional album artists filter
    /// </summary>
    [DataContract]
    public class BrowseArtistsRequest : PaginationRequest
    {
        [DataMember(Name = "album_artists")] public bool AlbumArtists { get; set; }
    }
}
