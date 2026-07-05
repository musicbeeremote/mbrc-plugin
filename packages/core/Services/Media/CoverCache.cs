using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Configuration;
using Newtonsoft.Json;

namespace MusicBeePlugin.Services.Media
{
    /// <summary>
    ///     Cover cache service implementation for storing and managing album cover cache.
    ///     Converted from singleton to dependency injection pattern.
    /// </summary>
    public class CoverCache : ICoverCache
    {
        private readonly ConcurrentDictionary<string, string> _covers = new ConcurrentDictionary<string, string>();
        private readonly IPluginLogger _logger;
        private readonly string _state;
        private readonly string _storagePath;
        private readonly object _buildLock = new object();
        private ConcurrentDictionary<string, string> _paths = new ConcurrentDictionary<string, string>();

        public CoverCache(IPluginLogger logger, string storagePath)
        {
            _logger = logger;
            _storagePath = storagePath;
            _state = Path.Combine(storagePath, "cache", "state.json");
        }

        /// <summary>
        ///     Gets the current state of the cache (number of cached items).
        /// </summary>
        public string State => _covers.Count.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        ///     Gets the number of items in the cache.
        /// </summary>
        public int Count => _covers.Count;

        /// <summary>
        ///     Builds the cache by processing missing covers and cleaning up orphaned files.
        /// </summary>
        /// <param name="cacheCover">Function to cache a cover for a given track path</param>
        public void Build(Func<string, string> cacheCover)
        {
            lock (_buildLock)
            {
                var missingCovers = _paths.Where(path =>
                    !_covers.ContainsKey(path.Key)
                ).ToList();

                foreach (var pair in missingCovers)
                    _covers[pair.Key] = cacheCover(pair.Value);

                _logger.Debug($"Added {missingCovers.Count} missing covers to cache");

                var cachedValues = new HashSet<string>(_covers.Values);
                foreach (var path in Directory.GetFiles(Utilities.Common.Utilities.GetCoverStoragePath(_storagePath)))
                    try
                    {
                        var filename = Path.GetFileName(path);
                        if (cachedValues.Contains(filename))
                            continue;
                        _logger.Debug($"Removing {filename} since it should not be in cache");
                        File.Delete(path);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, $"There was an error deleting {path}");
                    }

                PersistCache();
            }
        }

        /// <summary>
        ///     Checks if a cover is cached for the given key.
        /// </summary>
        /// <param name="key">The cache key (typically album identifier)</param>
        /// <returns>True if cached, false otherwise</returns>
        public bool IsCached(string key)
        {
            return _covers.ContainsKey(key);
        }

        /// <summary>
        ///     Gets all cache keys.
        /// </summary>
        /// <returns>Enumerable of all cache keys</returns>
        public IEnumerable<string> Keys()
        {
            return _covers.Keys;
        }

        /// <summary>
        ///     Gets the cover hash for the given key.
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>Cover hash or null if not found</returns>
        public string GetCoverHash(string key)
        {
            return _covers.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        ///     Updates the cache with a new cover hash for the given key.
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <param name="hash">The cover hash</param>
        public void Update(string key, string hash)
        {
            _covers[key] = hash;
        }

        /// <summary>
        ///     Looks up the file path for the given cache key.
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>File path or empty string if not found</returns>
        public string Lookup(string key)
        {
            return _paths.TryGetValue(key, out var path) ? path : string.Empty;
        }

        /// <summary>
        ///     Gets both the cover hash and file path for the given cache key in a single lookup.
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>Tuple of (hash, path) or (null, empty) if not found</returns>
        public (string Hash, string Path) GetCoverInfo(string key)
        {
            _covers.TryGetValue(key, out var hash);
            _paths.TryGetValue(key, out var path);
            return (hash, path ?? string.Empty);
        }

        /// <summary>
        ///     Warms up the cache with track paths and modification dates.
        /// </summary>
        /// <param name="paths">Dictionary of cache keys to file paths</param>
        /// <param name="modified">Dictionary of cache keys to modification dates</param>
        public void WarmUpCache(Dictionary<string, string> paths, Dictionary<string, string> modified)
        {
            lock (_buildLock)
            {
                _paths = new ConcurrentDictionary<string, string>(paths);
                var state = LoadCache();
                if (state.Covers == null)
                {
                    _logger.Debug("No cached state found. State needs to be build.");
                    return;
                }

                var cachedCovers = state.Covers;
                var lastCheck = DateTimeOffset.FromUnixTimeSeconds(state.LastCheck).DateTime;
                foreach (var path in paths)
                {
                    if (!cachedCovers.TryGetValue(path.Key, out var cover))
                        continue;
                    if (!DateTime.TryParse(modified[path.Key], out var lastModified))
                        continue;
                    if (DateTime.Compare(lastModified, lastCheck) >= 0)
                        continue;
                    _covers[path.Key] = cover;
                }
            }
        }

        /// <summary>
        ///     Invalidates the entire cache, clearing all data and removing cached files.
        /// </summary>
        public void Invalidate()
        {
            lock (_buildLock)
            {
                try
                {
                    if (File.Exists(_state))
                    {
                        _logger.Debug("Deleting cover state file");
                        File.Delete(_state);
                    }

                    var coversPath = Utilities.Common.Utilities.GetCoverStoragePath(_storagePath);
                    if (Directory.Exists(coversPath))
                    {
                        _logger.Debug("Deleting cached covers");
                        Directory.Delete(coversPath, true);
                    }

                    _covers.Clear();
                    _paths.Clear();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Invalidate cache failed");
                }
            }
        }

        private CoverCacheState LoadCache()
        {
            if (!File.Exists(_state))
                return new CoverCacheState();

            try
            {
                using (var sr = new StreamReader(_state))
                {
                    var jsonText = sr.ReadToEnd();
                    return JsonConvert.DeserializeObject<CoverCacheState>(jsonText);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to read from {_state}");
                return new CoverCacheState();
            }
        }

        private void PersistCache()
        {
            var state = new CoverCacheState
            {
                Covers = new Dictionary<string, string>(_covers),
                LastCheck = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds()
            };

            try
            {
                var backup = $"{_state}.bak";
                if (File.Exists(backup))
                    File.Delete(backup);

                if (File.Exists(_state))
                    File.Move(_state, backup);

                _logger.Debug($"Preparing to persist cache state to {_state}");

                using (var sw = File.CreateText(_state))
                {
                    var jsonText = JsonConvert.SerializeObject(state, Formatting.Indented);
                    sw.Write(jsonText);
                }

                if (File.Exists(backup))
                    File.Delete(backup);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not persist the state");
            }
        }
    }
}
