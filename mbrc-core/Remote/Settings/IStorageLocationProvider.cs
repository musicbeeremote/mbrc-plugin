namespace MusicBeeRemoteCore.Remote.Settings
{
    internal interface IStorageLocationProvider
    {
        string StorageLocation();
        string CacheLocation();
        string LogFile { get; }
    }

    class StorageLocationProvider : IStorageLocationProvider
    {
        private readonly string _location;

        public StorageLocationProvider(string location)
        {
            _location = location;
        }

        public string StorageLocation()
        {
            return _location;
        }

        public string CacheLocation()
        {
            return $"{_location}\\cache";
        }

        public string LogFile
        {
            get { return $"{_location}\\mbrc.log"; }
        }
    }
}