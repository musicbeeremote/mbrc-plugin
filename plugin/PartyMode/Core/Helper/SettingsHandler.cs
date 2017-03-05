using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using ServiceStack.Text;

namespace MusicBeePlugin.PartyMode.Core.Helper
{
    public class SettingsHandler
    {
        private readonly string _filePath;

        public SettingsHandler()
        {
            //Todo find a proper way to get the path
            //filePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _filePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\MusicBee\\mb_remote\\partymode_data.json";

            if (!File.Exists(_filePath))
            {
                var stream = File.Create(_filePath);
                stream.Close();
            }

            JsConfig<IPAddress>.SerializeFn = ipadr => ipadr.ToString();
            JsConfig<IPAddress>.DeSerializeFn = IPAddress.Parse;

            JsConfig<PhysicalAddress>.SerializeFn = phadr => phadr.ToString();
            JsConfig<PhysicalAddress>.DeSerializeFn = StrictParseAddress;
            //todo serialize date to epoch
        }

        public Settings GetSettings()
        {
            var filestring = LoadJson(_filePath);
            var settings = JsonSerializer.DeserializeFromString<Settings>(filestring) ?? new Settings();//.DeserializeObject<Settings>(filestring);

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
            var fileString = JsonSerializer.SerializeToString(settings);

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

            var validAddresses = settings.KnownAdresses
                .Where(x => DateTime.Compare(x.LastLogIn, storageIsOverDate) > 1)
                .ToList();

            if (validAddresses.Any()) settings.KnownAdresses = validAddresses;

            return settings;
        }
    }
}