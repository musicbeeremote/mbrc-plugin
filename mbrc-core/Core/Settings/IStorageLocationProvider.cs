using System.IO;

namespace MusicBeeRemote.Core.Settings
{
    public interface IStorageLocationProvider
    {
        string SettingsFile { get; }

        string LogFile { get; }

        string CacheDatabase { get; }

        string LimitedSettings { get; }

        string LogLocation();

        string StorageLocation();

        string CacheLocation();
    }

    public class StorageLocationProvider : IStorageLocationProvider
    {
        private const string RemoteDataDirectory = "mb_remote";
        private const string CacheSubDir = "cache";
        private const string LogFileName = "mbrc.log";
        private const string LogDirectory = "logs";
        private const string SettingsFileName = "settings.json";
        private const string LimitedSettingsFile = "limited_execution_settings.json";
        private const string ClientDatabase = "data.db3";
        private readonly string _location;

        public StorageLocationProvider(string location)
        {
            _location = $"{location}{Path.DirectorySeparatorChar}{RemoteDataDirectory}";
            Directory.CreateDirectory(CacheLocation());
            Directory.CreateDirectory(LogLocation());
        }

        /// <summary>
        ///     Gets the settings file location.
        /// </summary>
        /// <value>
        ///     The settings file location.
        /// </value>
        public string SettingsFile => $"{_location}{Path.DirectorySeparatorChar}{SettingsFileName}";

        public string CacheDatabase => $"{CacheLocation()}{Path.DirectorySeparatorChar}{ClientDatabase}";

        public string LimitedSettings => $"{_location}{Path.DirectorySeparatorChar}{LimitedSettingsFile}";

        /// <summary>
        ///     Gets the log file location.
        /// </summary>
        /// <value>
        ///     The log file location.
        /// </value>
        public string LogFile => $"{LogLocation()}{Path.DirectorySeparatorChar}{LogFileName}";

        public string LogLocation()
        {
            return $"{_location}{Path.DirectorySeparatorChar}{LogDirectory}";
        }

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
