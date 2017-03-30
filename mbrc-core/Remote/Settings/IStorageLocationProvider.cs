using System.IO;

namespace MusicBeeRemoteCore.Remote.Settings
{
    public interface IStorageLocationProvider
    {
        string StorageLocation();
        string CacheLocation();
        string LogFile { get; }
    }

    class StorageLocationProvider : IStorageLocationProvider
    {
        private readonly string _location;
        private const string RemoteDataDirectory = "mb_remote";
        private const string CacheSubDir = "cache";
        private const string LogFileName = "mbrc.log";

        public StorageLocationProvider(string location)
        {
            _location = $"{location}{Path.DirectorySeparatorChar}{RemoteDataDirectory}";
        }

        public string StorageLocation()
        {
            return _location;
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