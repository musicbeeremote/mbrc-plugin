using System.IO;

namespace MusicBeeRemoteCore.Core.Settings
{
    public interface IStorageLocationProvider
    {
        string StorageLocation();
        string SettingsFile { get; }
        string LegacySettingsFile { get; }
        string CacheLocation();
        string LogFile { get; }
    }

    class StorageLocationProvider : IStorageLocationProvider
    {
        private readonly string _location;
        private const string RemoteDataDirectory = "mb_remote";
        private const string CacheSubDir = "cache";
        private const string LogFileName = "mbrc.log";
        private const string SettingsFileName = "settings.json";
        private const string LegacySettingsFilename = "settings.json";

        public StorageLocationProvider(string location)
        {
            _location = $"{location}{Path.DirectorySeparatorChar}{RemoteDataDirectory}";
        }

        public string StorageLocation()
        {
            return _location;
        }

        public string SettingsFile
        {
            get { return $"{_location}{Path.DirectorySeparatorChar}{SettingsFileName}"; }
        }

        public string LegacySettingsFile
        {
            get { return $"{_location}{Path.DirectorySeparatorChar}{LegacySettingsFilename}"; }
        }

        public string CacheLocation()
        {
            return $"{_location}{Path.DirectorySeparatorChar}{CacheSubDir}";
        }

        public string LogFile
        {
            get { return $"{_location}{Path.DirectorySeparatorChar}{LogFileName}"; }
        }
    }
}