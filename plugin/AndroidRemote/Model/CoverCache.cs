using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using ServiceStack.Text;
using static MusicBeePlugin.AndroidRemote.Utilities.Utilities;

namespace MusicBeePlugin.AndroidRemote.Model
{
    public class CoverCache
    {
        private readonly Dictionary<string, string> _covers = new Dictionary<string, string>();

        /** Singleton **/
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly string _state = StoragePath + @"cache\state.json";
        private Dictionary<string, string> _paths = new Dictionary<string, string>();
        public static CoverCache Instance { get; } = new CoverCache();
        public string State => _covers.Count.ToString();

        public void Build(Func<string, string> cacheCover)
        {
            var missingCovers = _paths.Where(path =>
                !_covers.TryGetValue(path.Key, out _)
            ).ToList();
            foreach (var pair in missingCovers) _covers[pair.Key] = cacheCover(pair.Value);

            _logger.Debug($"Added {missingCovers.LongCount()} missing covers to cache");

            foreach (var path in Directory.GetFiles(CoverStorage))
                try
                {
                    var filename = Path.GetFileName(path);
                    if (_covers.ContainsValue(filename)) continue;
                    _logger.Debug($"Removing {filename} since it should not be in cache");
                    File.Delete(path);
                }
                catch (Exception e)
                {
                    _logger.Error(e, $"There was an error deleting {path}");
                }

            PersistCache();
        }

        public bool IsCached(string key)
        {
            return _covers.ContainsKey(key);
        }

        public List<string> Keys()
        {
            return _covers.Keys.ToList();
        }

        public string GetCoverHash(string key)
        {
            return _covers.Get(key);
        }

        public void Update(string key, string hash)
        {
            _covers[key] = hash;
        }

        public string Lookup(string key)
        {
            return _paths.TryGetValue(key, out var path) ? path : string.Empty;
        }

        private CoverCacheState LoadCache()
        {
            if (!File.Exists(_state)) return new CoverCacheState();

            try
            {
                using (var sr = new StreamReader(_state))
                {
                    return JsonSerializer.DeserializeFromReader<CoverCacheState>(sr);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read from {_state}");
                return new CoverCacheState();
            }
        }


        private void PersistCache()
        {
            var state = new CoverCacheState
            {
                Covers = _covers,
                LastCheck = DateTime.Now.ToUnixTime()
            };

            try
            {
                var backup = $"{_state}.bak";
                if (File.Exists(backup)) File.Delete(backup);

                if (File.Exists(_state)) File.Move(_state, backup);

                _logger.Debug($"Preparing to persist cache state to {_state}");

                using (var sw = File.CreateText(_state))
                {
                    JsonSerializer.SerializeToWriter(state, sw);
                }

                if (File.Exists(backup)) File.Delete(backup);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Could not persist the state");
            }
        }

        public void WarmUpCache(Dictionary<string, string> paths, Dictionary<string, string> modified)
        {
            _paths = paths;
            var state = LoadCache();
            if (state.Covers == null)
            {
                _logger.Debug("No cached state found. State needs to be build.");
                return;
            }

            var cachedCovers = state.Covers;
            var lastCheck = state.LastCheck.FromUnixTime();
            foreach (var path in paths)
            {
                if (!cachedCovers.TryGetValue(path.Key, out var cover)) continue;
                if (!DateTime.TryParse(modified[path.Key], out var lastModified)) continue;
                if (DateTime.Compare(lastModified, lastCheck) >= 0) continue;
                _covers[path.Key] = cover;
            }
        }

        public void Invalidate()
        {
            try
            {
                if (File.Exists(_state))
                {
                    _logger.Debug("Deleting cover state file");
                    File.Delete(_state);
                }

                var coversPath = StoragePath + @"cache\covers";
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
                _logger.Error(e, "Invalidate cache failed");
            }
        }
    }
}