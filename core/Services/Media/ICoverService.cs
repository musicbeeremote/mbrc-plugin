using System.Threading.Tasks;
using MusicBeePlugin.Models.Responses;

namespace MusicBeePlugin.Services.Media
{
    /// <summary>
    ///     Interface for cover service that handles album cover operations.
    ///     Manages cache building, cover retrieval, and status broadcasting.
    /// </summary>
    public interface ICoverService
    {
        /// <summary>
        ///     Gets whether the cache is currently being built.
        /// </summary>
        bool IsBuildingCache { get; }

        /// <summary>
        ///     Initializes the cover cache asynchronously.
        /// </summary>
        /// <returns>Task representing the cache initialization operation</returns>
        Task InitializeCacheAsync();

        /// <summary>
        ///     Invalidates the entire cover cache.
        /// </summary>
        void InvalidateCache();

        /// <summary>
        ///     Gets an album cover for the specified artist and album.
        /// </summary>
        /// <param name="artist">The artist name</param>
        /// <param name="album">The album name</param>
        /// <param name="clientHash">Optional client hash for cache validation</param>
        /// <returns>Album cover payload with status and data</returns>
        AlbumCoverPayload GetAlbumCover(string artist, string album, string clientHash = null);

        /// <summary>
        ///     Gets an album cover with specified size.
        /// </summary>
        /// <param name="artist">The artist name</param>
        /// <param name="album">The album name</param>
        /// <param name="size">The requested size ("original" or numeric pixel size)</param>
        /// <returns>Album cover payload with resized cover data</returns>
        AlbumCoverPayload GetCoverBySize(string artist, string album, string size);

        /// <summary>
        ///     Gets the cover for the currently playing track.
        /// </summary>
        /// <returns>Cover path or empty string if not available</returns>
        string GetNowPlayingCover();

        /// <summary>
        ///     Enumerate the album-cover cache as a paginated batch.
        ///     Drives the iOS-v4 <c>libraryalbumcover</c> request variant
        ///     <c>{offset,limit}</c>; returns one entry per cached cover
        ///     with <c>{album, artist, cover, hash, status}</c>.
        /// </summary>
        /// <param name="offset">The starting offset for pagination</param>
        /// <param name="limit">Maximum number of covers to return</param>
        /// <returns>List of cover entries plus pagination metadata</returns>
        AlbumCoverBatchPage GetAlbumCoverBatch(int offset, int limit);

        /// <summary>
        ///     Caches the cover for a newly added track.
        ///     Gets the track's artwork, generates cache key, and updates the cache.
        /// </summary>
        /// <param name="trackUrl">The file URL of the track</param>
        void CacheTrackCover(string trackUrl);
    }
}
