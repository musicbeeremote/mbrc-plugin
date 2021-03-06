using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using ServiceStack.Text;
using static MusicBeePlugin.AndroidRemote.Utilities.Utilities;

namespace MusicBeePlugin.AndroidRemote.Model
{
    public class CoverCache
    {
        /** Singleton **/
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private Dictionary<string, string> _covers = new Dictionary<string, string>();
        private Dictionary<string, string> _paths = new Dictionary<string, string>();
        public static CoverCache Instance { get; } = new CoverCache();

        public void SetPaths(Dictionary<string,string> paths)
        {
            _paths = paths;
        }

        public void Build(Func<string, string> cacheCover)
        {
            foreach (var pair in _paths)
            {
                _covers[pair.Key] = cacheCover(pair.Value);
            }

            foreach (var path in Directory.GetFiles(CoverStorage))
            {
                var filename = Path.GetFileName(path);
                if (_covers.ContainsValue(filename)) continue;
                _logger.Debug($"Deleting not found {filename}");
                File.Delete(path);
            }
        }

        public bool IsCached(string key)
        {
            return _covers.ContainsKey(key);
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
            return _paths.ContainsKey(key) ? _paths[key] : string.Empty;
        }
    }
}