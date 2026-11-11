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

        /// <summary>Whether artist pictures are being downloaded.</summary>
        public bool artist_images_enabled { get; set; }

        /// <summary>Artists with a stored picture.</summary>
        public int artist_images { get; set; }

        /// <summary>The disk the stored pictures use, in bytes.</summary>
        public long artist_image_bytes { get; set; }

        /// <summary>The disk they may use before downloads stop, in bytes.</summary>
        public long artist_image_budget { get; set; }

        /// <summary>Artists looked up and found to have no picture.</summary>
        public int artist_images_not_found { get; set; }

        /// <summary>Artists with no sure match, left without a picture.</summary>
        public int artist_images_ambiguous { get; set; }

        /// <summary>Artists whose last lookup failed, tried again later.</summary>
        public int artist_images_failed { get; set; }

        /// <summary>Artists waiting to be looked up.</summary>
        public int artist_images_waiting { get; set; }

        /// <summary>The smallest disk budget the core accepts, in MiB.</summary>
        public int artist_images_budget_min_mb { get; set; }

        /// <summary>The largest disk budget the core accepts, in MiB.</summary>
        public int artist_images_budget_max_mb { get; set; }
    }
}
