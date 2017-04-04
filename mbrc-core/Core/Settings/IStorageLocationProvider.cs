using System.IO;

namespace MusicBeeRemote.Core.Settings
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
        private const string LegacySettingsFilename = "settings.xml";

        public StorageLocationProvider(string location)
        {
            _location = $"{location}{Path.DirectorySeparatorChar}{RemoteDataDirectory}";
        }

        public string StorageLocation()
        {
            return _location;
        }

        /// <summary>
        /// Gets the settings file location.
        /// </summary>
        /// <value>
        /// The settings file location.
        /// </value>
        public string SettingsFile => $"{_location}{Path.DirectorySeparatorChar}{SettingsFileName}";

        /// <summary>
        /// Gets the legacy settings file full path.
        /// </summary>
        /// <value>
        /// The legacy settings file path.
        /// </value>
        public string LegacySettingsFile => $"{_location}{Path.DirectorySeparatorChar}{LegacySettingsFilename}";

        public string CacheLocation()
        {
            return $"{_location}{Path.DirectorySeparatorChar}{CacheSubDir}";
        }

        /// <summary>
        /// Gets the log file location.
        /// </summary>
        /// <value>
        /// The log file location.
        /// </value>
        public string LogFile => $"{_location}{Path.DirectorySeparatorChar}{LogFileName}";
    }
}