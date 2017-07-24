using System;
using System.IO;
using System.Linq;
using MusicBeeRemote.Core.Settings;
using Newtonsoft.Json;

namespace MusicBeeRemote.PartyMode.Core.Helper
{
    public class SettingsHandler
    {
        private readonly string _filePath;
        private const string PartySettingsFile = "partymode_data.json";

        public SettingsHandler(IStorageLocationProvider storageLocationProvider)
        {

            _filePath = $"{storageLocationProvider.StorageLocation()}{Path.DirectorySeparatorChar}{PartySettingsFile}";


            if (!File.Exists(_filePath))
            {
                var stream = File.Create(_filePath);
                stream.Close();
            }
        }

        public Settings GetSettings()
        {
            var filestring = LoadJson(_filePath);
            var settings = JsonConvert.DeserializeObject<Settings>(filestring) ?? new Settings();

            return ValidateSettings(settings);
        }

        public void SaveSettings(Settings settings)
        {
            SaveJson(_filePath, settings);
        }

        private static string LoadJson(string path)
        {
            string json;

            using (var sr = new StreamReader(path))
            {
                json = sr.ReadToEnd();
            }

            return json;
        }

        private static void SaveJson(string path, Settings settings)
        {
            var fileString = JsonConvert.SerializeObject(settings);

            using (var sw = new StreamWriter(path))
            {
                sw.Write(fileString);
                sw.Flush();
                sw.Close();
            }
        }

        private static Settings ValidateSettings(Settings settings)
        {    
            return settings;
        }
    }
}