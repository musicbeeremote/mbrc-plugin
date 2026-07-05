using System;
using System.Collections.Generic;

namespace MusicBeePlugin.Services.Media
{
    /// <summary>
    ///     Interface for cover cache storage and management.
    ///     Handles in-memory caching, persistence, and cache operations for album covers.
    /// </summary>
    public interface ICoverCache
    {
        /// <summary>
        ///     Gets the current state of the cache (number of cached items).
        /// </summary>
        string State { get; }

        /// <summary>
        ///     Gets the number of items in the cache.
        /// </summary>
        int Count { get; }

        /// <summary>
        ///     Builds the cache by processing missing covers and cleaning up orphaned files.
        /// </summary>
        /// <param name="cacheCover">Function to cache a cover for a given track path</param>
        void Build(Func<string, string> cacheCover);

        /// <summary>
        ///     Checks if a cover is cached for the given key.
        /// </summary>
        /// <param name="key">The cache key (typically album identifier)</param>
        /// <returns>True if cached, false otherwise</returns>
        bool IsCached(string key);

        /// <summary>
        ///     Gets all cache keys.
        /// </summary>
        /// <returns>Enumerable of all cache keys</returns>
        IEnumerable<string> Keys();

        /// <summary>
        ///     Gets the cover hash for the given key.
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>Cover hash or null if not found</returns>
        string GetCoverHash(string key);

        /// <summary>
        ///     Updates the cache with a new cover hash for the given key.
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <param name="hash">The cover hash</param>
        void Update(string key, string hash);

        /// <summary>
        ///     Looks up the file path for the given cache key.
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>File path or empty string if not found</returns>
        string Lookup(string key);

        /// <summary>
        ///     Gets both the cover hash and file path for the given cache key in a single lookup.
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>Tuple of (hash, path) or (null, empty) if not found</returns>
        (string Hash, string Path) GetCoverInfo(string key);

        /// <summary>
        ///     Warms up the cache with track paths and modification dates.
        /// </summary>
        /// <param name="paths">Dictionary of cache keys to file paths</param>
        /// <param name="modified">Dictionary of cache keys to modification dates</param>
        void WarmUpCache(Dictionary<string, string> paths, Dictionary<string, string> modified);

        /// <summary>
        ///     Invalidates the entire cache, clearing all data and removing cached files.
        /// </summary>
        void Invalidate();
    }
}
