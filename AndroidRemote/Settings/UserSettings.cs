using System.Globalization;
using System.IO;
using System.Xml;
using MusicBeePlugin.AndroidRemote.Model;

namespace MusicBeePlugin.AndroidRemote.Settings
{
    internal static class UserSettings
    {
        public static string SettingsFilePath { get; set; }
        public static string SettingsFileName { get; set; }
        private static SettingsModel _settingsModel;
        private const string PortNumber = "port";
        private const string Values = "values";
        private const string Selection = "selection";
        
        public static SettingsModel SettingsModel
        {
            get { return _settingsModel; }
            set { _settingsModel = value; }
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
            if (_settingsModel.ListeningPort > 0 && _settingsModel.ListeningPort < 65535)
                WriteNodeValue(document, PortNumber, _settingsModel.ListeningPort.ToString(CultureInfo.InvariantCulture));
            WriteNodeValue(document, Values, _settingsModel.GetValues());
            WriteNodeValue(document, Selection, _settingsModel.FilterSelection.ToString());
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
            if(_settingsModel==null)
            {
                _settingsModel = new SettingsModel();
            }
            if (!File.Exists(SettingsFilePath + SettingsFileName))
            {
                _settingsModel.ListeningPort = 3000;
            }
            else
            {
                XmlDocument document = new XmlDocument();
                document.Load(SettingsFilePath + SettingsFileName);
                int listeningPort;
                _settingsModel.ListeningPort = int.TryParse(ReadNodeValue(document, PortNumber), out listeningPort) ? listeningPort : 3000;
                _settingsModel.UpdateFilteringSelection(ReadNodeValue(document, Selection));
                _settingsModel.SetValues(ReadNodeValue(document,Values));
            }
        }


    }
}