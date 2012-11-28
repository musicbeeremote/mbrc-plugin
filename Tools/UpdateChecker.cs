using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace MusicBeePlugin.Tools
{
    internal class UpdateChecker
    {
        private const string FilePath = "\\mb_remote\\lastupdate.xml";

        public static bool IsThereAnUpdate(string version, string storagePath)
        {
            if (!ShouldICheckForUpdates(storagePath))
                return false;
            XmlDocument xDoc = new XmlDocument();
            try
            {
                xDoc.Load("http://kelsos.net/musicbeeremote/latest.xml");
            }
            catch (Exception)
            {
                return false;
            }
            XmlNode versionNode = xDoc.SelectSingleNode("//version");
            if (versionNode != null)
            {
                SetUpdateCheckTime(storagePath);
                return
                    !version.Equals(versionNode.InnerText.Substring(0,
                                                                    versionNode.InnerText.LastIndexOf(".",
                                                                                                      StringComparison.
                                                                                                          Ordinal)));
            }
            return false;
        }

        private static void SetUpdateCheckTime(string storagePath)
        {
            string settingsFile = storagePath + FilePath;
            XmlDocument document = new XmlDocument();
            document.Load(settingsFile);
            XmlNode node = document.SelectSingleNode("//date");
            if (node == null)
            {
                XmlElement pattern = document.CreateElement("date");
                XmlNode root = document.DocumentElement;
                pattern.InnerText = DateTime.Now.ToString(CultureInfo.InvariantCulture);
                if (root != null) root.AppendChild(pattern);
            }
            else
            {
                node.InnerText = DateTime.Now.ToString(CultureInfo.InvariantCulture);
            }
            document.Save(settingsFile);
        }

        private static bool ShouldICheckForUpdates(string storagePath)
        {
            string settingsFile = storagePath + FilePath;
            if (!File.Exists(settingsFile))
            {
                XmlDocument document = new XmlDocument();
                XmlDeclaration declaration = document.CreateXmlDeclaration("1.0", "utf-8", "yes");
                XmlElement root = document.CreateElement("lastUpdate");
                document.InsertBefore(declaration, document.DocumentElement);
                document.AppendChild(root);
                document.Save(settingsFile);
                return true;
            }
            else
            {
                XmlDocument document = new XmlDocument();
                document.Load(settingsFile);
                XmlNode lastUpdateTime = document.SelectSingleNode("//date");
                if (lastUpdateTime != null)
                {
                    TimeSpan difference = DateTime.Now.Subtract(DateTime.Parse(lastUpdateTime.InnerText));
                    if (difference.Days > 2)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}