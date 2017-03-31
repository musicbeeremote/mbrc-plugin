using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using MusicBeeRemoteCore.Core.Settings;
using Newtonsoft.Json;

namespace MusicBeeRemoteCore.PartyMode.Core.Helper
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

            //todo figure out how this customization would work with
//            JsConfig<IPAddress>.SerializeFn = ipadr => ipadr.ToString();
//            JsConfig<IPAddress>.DeSerializeFn = IPAddress.Parse;
//
//            JsConfig<PhysicalAddress>.SerializeFn = phadr => phadr.ToString();
//            JsConfig<PhysicalAddress>.DeSerializeFn = StrictParseAddress;
            //todo serialize date to epoch
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

        /// <summary>
        /// https://msdn.microsoft.com/en-us/library/system.net.networkinformation.physicaladdress.parse(v=vs.110).aspx
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static PhysicalAddress StrictParseAddress(string address)
        {
            var newAddress = PhysicalAddress.Parse(address);
            return PhysicalAddress.None.Equals(newAddress) ? null : newAddress;
        }

        private static Settings ValidateSettings(Settings settings)
        {
            var storageIsOverDate = DateTime.Now.AddDays(settings.AddressStoreDays * -1);

            var validAddresses = settings.KnownClients
                .Where(x => DateTime.Compare(x.LastLogIn, storageIsOverDate) > 1)
                .ToList();

            if (validAddresses.Any()) settings.KnownClients = validAddresses;

            return settings;
        }
    }
}