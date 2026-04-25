using System.Collections.Generic;

namespace MusicBeePlugin.Models.Responses
{
    /// <summary>
    ///     One row of an album-cover batch enumeration. Field names
    ///     match the wire shape (and the matching Rust DTO key names);
    ///     do not rename without updating the Rust side and drift
    ///     fixtures together.
    /// </summary>
    public sealed class AlbumCoverBatchEntry
    {
        public string Album { get; set; }
        public string Artist { get; set; }
        public string Cover { get; set; }
        public string Hash { get; set; }
        public int Status { get; set; }
    }

    /// <summary>
    ///     Paginated cover-cache enumeration returned to the iOS-v4
    ///     <c>libraryalbumcover</c> batch query. Marshalled across the
    ///     FFI as MessagePack and converted to the Rust-side
    ///     <c>AlbumCoverBatchResponse</c>.
    /// </summary>
    public sealed class AlbumCoverBatchPage
    {
        public List<AlbumCoverBatchEntry> Data { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int Total { get; set; }
    }
}
