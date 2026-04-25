using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Responses;

namespace MusicBeePlugin.Services.Media
{
    /// <summary>
    ///     Service implementation for cover operations.
    ///     Handles cache management, cover retrieval, and status broadcasting.
    /// </summary>
    public class CoverService : ICoverService
    {
        private readonly ICoverCache _coverCache;
        private readonly ILibraryDataProvider _libraryDataProvider;
        private readonly IPluginLogger _logger;
        private readonly string _storagePath;
        private readonly ISystemOperations _systemOperations;
        private readonly ITrackDataProvider _trackDataProvider;

        public CoverService(
            ICoverCache coverCache,
            ILibraryDataProvider libraryDataProvider,
            ITrackDataProvider trackDataProvider,
            ISystemOperations systemOperations,
            IPluginLogger logger,
            string storagePath)
        {
            _coverCache = coverCache;
            _libraryDataProvider = libraryDataProvider;
            _trackDataProvider = trackDataProvider;
            _systemOperations = systemOperations;
            _logger = logger;
            _storagePath = storagePath;
        }

        /// <summary>
        ///     Gets whether the cache is currently being built.
        /// </summary>
        public bool IsBuildingCache { get; private set; }

        /// <summary>
        ///     Initializes the cover cache asynchronously.
        /// </summary>
        public Task InitializeCacheAsync()
        {
            return Task.Run(() =>
            {
                IsBuildingCache = true;
                _systemOperations.SetBackgroundTaskMessage("MusicBee Remote: Caching album covers.");

                PrepareCache();
                BuildCache();

                IsBuildingCache = false;
                _systemOperations.SetBackgroundTaskMessage(
                    $"MusicBee Remote: Done. {_coverCache.State} album covers are now cached.");
            });
        }

        /// <summary>
        ///     Invalidates the entire cover cache.
        /// </summary>
        public void InvalidateCache()
        {
            _coverCache.Invalidate();
            // Don't use .Wait() as it can cause deadlocks - let the task run asynchronously
            _systemOperations.CreateBackgroundTask(() => InitializeCacheAsync());
        }

        /// <summary>
        ///     Gets an album cover for the specified artist and album.
        /// </summary>
        public AlbumCoverPayload GetAlbumCover(string artist, string album, string clientHash = null)
        {
            // Match v1.4.1 behavior: return 400 for empty album
            if (string.IsNullOrEmpty(album))
                return GetAlbumCoverStatusPayload(400);

            var key = Utilities.Common.Utilities.CoverIdentifier(artist, album);
            string hash;

            if (_coverCache.IsCached(key))
            {
                hash = _coverCache.GetCoverHash(key);
                return string.IsNullOrEmpty(hash)
                    ? GetAlbumCoverStatusPayload(404)
                    : GetAlbumCoverFromCache(clientHash, hash);
            }

            var path = _coverCache.Lookup(key);
            if (string.IsNullOrEmpty(path))
                return GetAlbumCoverStatusPayload(404);

            hash = CacheCover(path);
            if (string.IsNullOrEmpty(hash))
                return GetAlbumCoverStatusPayload(404);

            _coverCache.Update(key, hash);
            return GetAlbumCoverFromCache(clientHash, hash);
        }

        /// <summary>
        ///     Gets an album cover with specified size.
        /// </summary>
        public AlbumCoverPayload GetCoverBySize(string artist, string album, string size)
        {
            // Match v1.4.1 behavior: return 400 for empty album
            if (string.IsNullOrEmpty(album))
                return GetAlbumCoverStatusPayload(400);

            var isNumeric = int.TryParse(size, out var coverSize);
            var original = size == "original";

            if (!original && !isNumeric)
                return GetAlbumCoverStatusPayload(400);

            var key = Utilities.Common.Utilities.CoverIdentifier(artist, album);
            var path = _coverCache.Lookup(key);

            if (string.IsNullOrEmpty(path))
                return GetAlbumCoverStatusPayload(404);

            var artworkData = _libraryDataProvider.GetArtworkDataForTrack(path);
            var artworkPath = _libraryDataProvider.GetArtworkForTrack(path);

            if (!string.IsNullOrEmpty(artworkPath))
                return new AlbumCoverPayload
                {
                    Cover = original
                        ? Utilities.Common.Utilities.FileToBase64(artworkPath)
                        : Utilities.Common.Utilities.ImageResizeFile(artworkPath, coverSize, coverSize),
                    Status = 200,
                    Hash = Utilities.Common.Utilities.Sha1HashFile(artworkPath)
                };

            if (artworkData?.Length > 0)
                return new AlbumCoverPayload
                {
                    Cover = original
                        ? Convert.ToBase64String(artworkData)
                        : Utilities.Common.Utilities.ImageResize(artworkData, coverSize, coverSize),
                    Status = 200,
                    Hash = Utilities.Common.Utilities.Sha1Hash(artworkData)
                };

            return GetAlbumCoverStatusPayload(404);
        }


        /// <summary>
        ///     Gets the cover for the currently playing track.
        /// </summary>
        public string GetNowPlayingCover()
        {
            try
            {
                // First try to get the artwork directly
                var artwork = _trackDataProvider.GetNowPlayingArtwork();
                if (!string.IsNullOrEmpty(artwork))
                    return artwork;

                // If no direct artwork, try to get downloaded artwork
                var downloadedArtwork = _trackDataProvider.GetNowPlayingDownloadedArtwork();
                if (!string.IsNullOrEmpty(downloadedArtwork))
                    return downloadedArtwork;

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get now playing cover");
                return string.Empty;
            }
        }

        public void CacheTrackCover(string trackUrl)
        {
            try
            {
                var hash = CacheCover(trackUrl);
                var artist = _libraryDataProvider.GetAlbumArtistForTrack(trackUrl);
                var album = _libraryDataProvider.GetAlbumForTrack(trackUrl);
                var key = Utilities.Common.Utilities.CoverIdentifier(artist, album);
                _coverCache.Update(key, hash);

                _logger.Debug($"Cached cover for track: {trackUrl}, artist: {artist}, album: {album}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to cache cover for track: {trackUrl}");
            }
        }

        private void PrepareCache()
        {
            var watch = Stopwatch.StartNew();
            var identifiers = _libraryDataProvider.GetAllAlbumIdentifiers();

            _logger.Debug($"Detected {identifiers.Count} albums");

            var paths = _libraryDataProvider.GetTrackPaths();
            var modified = _libraryDataProvider.GetFileModificationDates();

            _coverCache.WarmUpCache(paths, modified);
            watch.Stop();
            _logger.Debug($"Cover cache preparation: {watch.ElapsedMilliseconds} ms");
        }

        private void BuildCache()
        {
            var watch = Stopwatch.StartNew();
            _coverCache.Build(CacheCover);
            watch.Stop();
            _logger.Debug($"Cover cache task complete after: {watch.ElapsedMilliseconds} ms");
        }

        private string CacheCover(string track)
        {
            var artworkData = _libraryDataProvider.GetArtworkDataForTrack(track);
            var artworkPath = _libraryDataProvider.GetArtworkForTrack(track);

            if (!string.IsNullOrEmpty(artworkPath))
                return Utilities.Common.Utilities.StoreCoverToCache(_storagePath, artworkPath);

            var hash = artworkData?.Length > 0
                ? Utilities.Common.Utilities.StoreCoverToCache(_storagePath, artworkData)
                : string.Empty;
            return hash;
        }

        private static AlbumCoverPayload GetAlbumCoverStatusPayload(int status)
        {
            return new AlbumCoverPayload
            {
                Status = status
            };
        }

        private AlbumCoverPayload GetAlbumCoverFromCache(string clientHash, string hash)
        {
            if (string.IsNullOrEmpty(clientHash) || clientHash != hash)
                return new AlbumCoverPayload
                {
                    Cover = Utilities.Common.Utilities.GetCoverFromCache(_storagePath, hash),
                    Status = 200,
                    Hash = hash
                };

            return GetAlbumCoverStatusPayload(304);
        }
    }
}
