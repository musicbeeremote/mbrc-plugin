using System.IO;
using System.Xml;

namespace MusicBeePlugin
{
    internal class UserSettings
    {
        public static string SettingsFilePath { get; set; }
        public static string SettingsFileName { get; set; }
        public static string ListeningPort { get; set; }

        /// <summary>
        /// Writes an XML node.
        /// </summary>
        /// <param name="xmlDoc">The XML document.</param>
        /// <param name="nodeName">Name of the node.</param>
        /// <param name="value">The value.</param>
        /// <remarks></remarks>
        private static void WriteXmlNode(XmlDocument xmlDoc, string nodeName, string value)
        {
            XmlNode node = xmlDoc.SelectSingleNode("//" + nodeName);
            if (node == null)
            {
                XmlElement pattern = xmlDoc.CreateElement(nodeName);
                XmlNode root = xmlDoc.DocumentElement;
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
        private static void SaveSettings()
        {
            if (!File.Exists(SettingsFilePath + SettingsFileName))
            {
                XmlDocument xmNew = new XmlDocument();

                //Writing the XML Declaration
                XmlDeclaration xmlDec = xmNew.CreateXmlDeclaration("1.0", "utf-8", "yes");

                //Creating the root element
                XmlElement rootNode = xmNew.CreateElement("Settings");
                xmNew.InsertBefore(xmlDec, xmNew.DocumentElement);
                xmNew.AppendChild(rootNode);
                xmNew.Save(SettingsFilePath + SettingsFileName);
            }
            XmlDocument xmD = new XmlDocument();
            xmD.Load(SettingsFilePath + SettingsFileName);
            //WriteXmlNode(xmD, "pattern", _nowPlayingPattern);
            //WriteXmlNode(xmD, "displaynote", _displayNote.ToString());
            //WriteXmlNode(xmD, "displayNowPlaying", _displayNowPlayingString.ToString());
            xmD.Save(SettingsFilePath + SettingsFileName);
        }

        /// <summary>
        /// Loads the settings.
        /// </summary>
        /// <remarks></remarks>
        private static void LoadSettings()
        {
            if (!File.Exists(SettingsFilePath + SettingsFileName))
            {
                //_nowPlayingPattern = "<Artist> - <Title>";
                //_displayNote = true;
                //_displayNowPlayingString = true;
            }
            else
            {
                //XmlDocument xmD = new XmlDocument();
                //xmD.Load(_settingFile);
                //_nowPlayingPattern = ReadPatternFromXml(xmD, "pattern");
                //_displayNote = ReadBooleanValuesFromXml(xmD, "displaynote");
                //_displayNowPlayingString = ReadBooleanValuesFromXml(xmD, "displayNowPlaying");
            }
        }
    }
}