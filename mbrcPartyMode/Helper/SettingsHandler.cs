using ServiceStack.Text;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace mbrcPartyMode.Helper
{
    public class SettingsHandler
    {
        private string filePath;

        public SettingsHandler()
        {
            //filePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            filePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\MusicBee\\mb_remote\\partyModeSettings.json";

            if (!File.Exists(filePath))
            {
                var stream = File.Create(filePath);
                stream.Close();
            }

            JsConfig<IPAddress>.SerializeFn = ipadr => ipadr.ToString();
            JsConfig<IPAddress>.DeSerializeFn = IPAddress.Parse;

            JsConfig<PhysicalAddress>.SerializeFn = phadr => phadr.ToString();
            JsConfig<PhysicalAddress>.DeSerializeFn = StrictParseAddress;
        }

        public Settings GetSettings()
        {
            var filestring = LoadJson(this.filePath);
            var settings = JsonSerializer.DeserializeFromString<Settings>(filestring);//.DeserializeObject<Settings>(filestring);

            if (settings == null) settings = new Settings();

            return ValidateSettings(settings);
        }

        public void SaveSettings(Settings settings)
        {
            this.SaveJson(filePath, settings);
        }

        private string LoadJson(string path)
        {
            string json;

            using (StreamReader sr = new StreamReader(path))
            {
                json = sr.ReadToEnd();
            }

            return json;
        }

        private void SaveJson(string path, Settings settings)
        {
            string fileString = JsonSerializer.SerializeToString<Settings>(settings);


            using (StreamWriter sw = new StreamWriter(path))
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
            PhysicalAddress newAddress = PhysicalAddress.Parse(address);
            if (PhysicalAddress.None.Equals(newAddress))
                return null;

            return newAddress;
        }

        private Settings ValidateSettings(Settings settings)
        {
            DateTime storageIsOverDate = DateTime.Now.AddDays(settings.AddressStoreDays * -1);

            var validAddresses = settings.KnownAdresses.Where(x => DateTime.Compare(x.LastLogIn, storageIsOverDate) > 1);

            if (validAddresses != null && validAddresses.Count() > 0) settings.KnownAdresses = validAddresses.ToList<ClientAdress>();

            return settings;
        }
    }
}