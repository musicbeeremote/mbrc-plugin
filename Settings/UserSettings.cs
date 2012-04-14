using System.Globalization;
using System.IO;
using System.Xml;

namespace MusicBeePlugin.Settings
{
    internal static class UserSettings
    {
        public static string SettingsFilePath { get; set; }
        public static string SettingsFileName { get; set; }
        private static ApplicationSettings _applicationSettings;
        private const string PortNumber = "portNumber";
        private const string AllowedAddresses = "allowedAddresses";
        private const string HostTypeSelection = "cbSelection";
        private const string StartingIpAddress = "startingIp";
        private const string MaxIpAddress = "maxIp";

        public static ApplicationSettings Settings
        {
            get { return _applicationSettings; }
            set { _applicationSettings = value; }
        }

        /// <summary>
        /// Writes an XML node.
        /// </summary>
        /// <param name="document">The XML document.</param>
        /// <param name="name">Name of the node.</param>
        /// <param name="value">The value.</param>
        /// <remarks></remarks>
        private static void WriteNodeValue(XmlDocument document, string name, string value)
        {
            XmlNode node = document.SelectSingleNode("//" + name);
            if (node == null)
            {
                XmlElement pattern = document.CreateElement(name);
                XmlNode root = document.DocumentElement;
                pattern.InnerText = value;
                if (root != null) root.AppendChild(pattern);
            }
            else
            {
                node.InnerText = value;
            }
        }

        /// <summary>
        /// Saves the settings.
        /// </summary>
        /// <remarks></remarks>
        public static void SaveSettings(string application)
        {
            if (!Directory.Exists(SettingsFileName + "mb_remote"))
                Directory.CreateDirectory(SettingsFilePath + "mb_remote");
            if (!File.Exists(SettingsFilePath + SettingsFileName))
                CreateEmptySettingsFile(application);
            XmlDocument document = new XmlDocument();
            document.Load(SettingsFilePath + SettingsFileName);
            WriteApplicationSetting(document);
            document.Save(SettingsFilePath + SettingsFileName);
        }

        private static void CreateEmptySettingsFile(string application)
        {
            XmlDocument document = new XmlDocument();
            XmlDeclaration declaration = document.CreateXmlDeclaration("1.0", "utf-8", "yes");
            XmlElement root = document.CreateElement(application);
            document.InsertBefore(declaration, document.DocumentElement);
            document.AppendChild(root);
            document.Save(SettingsFilePath + SettingsFileName);
        }

        private static void WriteApplicationSetting(XmlDocument document)
        {
            if (_applicationSettings.ListeningPort > 0 && _applicationSettings.ListeningPort < 65535)
                WriteNodeValue(document, PortNumber, _applicationSettings.ListeningPort.ToString(CultureInfo.InvariantCulture));
            WriteNodeValue(document, AllowedAddresses, _applicationSettings.FlattenAllowedAddressList());
            WriteNodeValue(document, HostTypeSelection, _applicationSettings.FilterSelection.ToString());
            WriteNodeValue(document,StartingIpAddress,_applicationSettings.BaseIp);
            WriteNodeValue(document,MaxIpAddress,_applicationSettings.LastOctetMax.ToString(CultureInfo.InvariantCulture));
        }

        private static string ReadNodeValue(XmlNode document, string name)
        {
            XmlNode node = document.SelectSingleNode("//" + name);
            return node != null ? node.InnerText : string.Empty;
        }

        /// <summary>
        /// Loads the settings.
        /// </summary>
        /// <remarks></remarks>
        public static void LoadSettings()
        {
            if(_applicationSettings==null)
            {
                _applicationSettings = new ApplicationSettings();
            }
            if (!File.Exists(SettingsFilePath + SettingsFileName))
            {
                _applicationSettings.ListeningPort = 3000;
            }
            else
            {
                XmlDocument document = new XmlDocument();
                document.Load(SettingsFilePath + SettingsFileName);
                int listeningPort, lastOctetMax;
                _applicationSettings.ListeningPort = int.TryParse(ReadNodeValue(document, PortNumber), out listeningPort) ? listeningPort : 3000;
                _applicationSettings.LastOctetMax = int.TryParse(ReadNodeValue(document, MaxIpAddress), out lastOctetMax)? lastOctetMax:0;
                _applicationSettings.BaseIp = ReadNodeValue(document, StartingIpAddress);
                _applicationSettings.UpdateFilteringSelection(ReadNodeValue(document, HostTypeSelection));
                _applicationSettings.UnflattenAllowedAddressList(ReadNodeValue(document,AllowedAddresses));
            }
        }


    }
}