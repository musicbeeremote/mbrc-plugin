namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     Cache health read from the Rust core via <c>mbrc_query</c>
    ///     (<c>HostQueryType.CacheStatus</c>). Property names are the MessagePack
    ///     keys and MUST match the Rust <c>CacheStatus</c> struct field names
    ///     (contractless resolver reads by name).
    /// </summary>
    public sealed class CoreCacheStatus
    {
        /// <summary>Tracks in the cached browse list (0 if never cached).</summary>
        public int tracks_cached { get; set; }

        /// <summary>Albums with a cached, resized cover.</summary>
        public int covers_cached { get; set; }

        /// <summary>A reconcile / cover-cache build is currently running.</summary>
        public bool building { get; set; }

        /// <summary>The metadata (browse) cache is validated and serving.</summary>
        public bool metadata_ready { get; set; }
    }
}
