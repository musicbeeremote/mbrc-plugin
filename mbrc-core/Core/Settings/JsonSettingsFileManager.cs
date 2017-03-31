using System.IO;
using Newtonsoft.Json;

namespace MusicBeeRemote.Core.Settings
{
    class JsonSettingsFileManager : IJsonSettingsFileManager
    {
        private readonly string _storageFilePath;

        public JsonSettingsFileManager(IStorageLocationProvider storageLocationProvider)
        {
            _storageFilePath = storageLocationProvider.SettingsFile;
        }

        public void Save(UserSettingsModel model)
        {
            var settings = JsonConvert.SerializeObject(model);
            if (_storageFilePath == null)
            {
                return;
            }

            File.WriteAllText(_storageFilePath, settings);
        }

        public UserSettingsModel Load()
        {
            if (!File.Exists(_storageFilePath))
            {
                return new UserSettingsModel();
            }

            var sr = File.OpenText(_storageFilePath);
            var settings = sr.ReadToEnd();
            sr.Close();
            return !string.IsNullOrEmpty(settings)
                ? JsonConvert.DeserializeObject<UserSettingsModel>(settings)
                : new UserSettingsModel();
        }
    }
}