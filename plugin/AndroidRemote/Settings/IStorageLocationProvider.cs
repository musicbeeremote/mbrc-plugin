namespace MusicBeePlugin.AndroidRemote.Settings
{
    internal interface IStorageLocationProvider
    {
        string StorageLocation();
        string CacheLocation();
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
    }
}