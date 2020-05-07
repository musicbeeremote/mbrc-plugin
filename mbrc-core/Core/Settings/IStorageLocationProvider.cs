using System.IO;

namespace MusicBeeRemote.Core.Settings
{
    public interface IStorageLocationProvider
    {
        string SettingsFile { get; }

        string LegacySettingsFile { get; }

        string LogFile { get; }

        string CacheDatabase { get; }

        string LimitedSettings { get; }

        string StorageLocation();

        string CacheLocation();
    }

    internal class StorageLocationProvider : IStorageLocationProvider
    {
        private const string RemoteDataDirectory = "mb_remote";
        private const string CacheSubDir = "cache";
        private const string LogFileName = "mbrc.log";
        private const string SettingsFileName = "settings.json";
        private const string LimitedSettingsFile = "limited_execution_settings.json";
        private const string LegacySettingsFilename = "settings.xml";
        private const string ClientDatabase = "data.db3";
        private readonly string _location;

        public StorageLocationProvider(string location)
        {
            _location = $"{location}{Path.DirectorySeparatorChar}{RemoteDataDirectory}";
            Directory.CreateDirectory(CacheLocation());
        }

        /// <summary>
        ///     Gets the settings file location.
        /// </summary>
        /// <value>
        ///     The settings file location.
        /// </value>
        public string SettingsFile => $"{_location}{Path.DirectorySeparatorChar}{SettingsFileName}";

        /// <summary>
        ///     Gets the legacy settings file full path.
        /// </summary>
        /// <value>
        ///     The legacy settings file path.
        /// </value>
        public string LegacySettingsFile => $"{_location}{Path.DirectorySeparatorChar}{LegacySettingsFilename}";

        public string CacheDatabase => $"{CacheLocation()}{Path.DirectorySeparatorChar}{ClientDatabase}";

        public string LimitedSettings => $"{_location}{Path.DirectorySeparatorChar}{LimitedSettingsFile}";

        /// <summary>
        ///     Gets the log file location.
        /// </summary>
        /// <value>
        ///     The log file location.
        /// </value>
        public string LogFile => $"{_location}{Path.DirectorySeparatorChar}{LogFileName}";

        public string CacheLocation()
        {
            return $"{_location}{Path.DirectorySeparatorChar}{CacheSubDir}";
        }

        public string StorageLocation()
        {
            return _location;
        }
    }
}
