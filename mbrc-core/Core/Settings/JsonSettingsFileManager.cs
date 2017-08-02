using System.IO;
using Newtonsoft.Json;

namespace MusicBeeRemote.Core.Settings
{
    class JsonSettingsFileManager : IJsonSettingsFileManager
    {
        private readonly string _storageFilePath;
        private readonly string _limitedFilePath;

        public JsonSettingsFileManager(IStorageLocationProvider storageLocationProvider)
        {
            _storageFilePath = storageLocationProvider.SettingsFile;
            _limitedFilePath = storageLocationProvider.LimitedSettings;
        }

        public void Save(UserSettingsModel model)
        {
            Save(model, _storageFilePath);
        }

        public UserSettingsModel Load()
        {
            return Load<UserSettingsModel>(_storageFilePath);
        }

        public void SaveLimitedModeSettingsModel(LimitedModeSettingsModel model)
        {
            Save(model, _limitedFilePath);
        }

        public LimitedModeSettingsModel LoadLimitedModeSettingsModel()
        {
            return Load<LimitedModeSettingsModel>(_limitedFilePath);
        }

        private static void Save<T>(T model, string path)
        {
            var settings = JsonConvert.SerializeObject(model);
            if (path == null)
            {
                return;
            }

            File.WriteAllText(path, settings);
        }

        private T Load<T>(string path) where T : new()
        {
            if (!File.Exists(path))
            {
                return new T();
            }

            var sr = File.OpenText(_storageFilePath);
            var settings = sr.ReadToEnd();
            sr.Close();
            return !string.IsNullOrEmpty(settings) ? JsonConvert.DeserializeObject<T>(settings) : new T();
        }
    }
}