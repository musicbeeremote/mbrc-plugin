using System.Globalization;
using System.IO;
using System.Xml;
using MusicBeePlugin.AndroidRemote.Events;

namespace MusicBeePlugin.AndroidRemote.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Represents the settings along with all the settings related functionality
    /// </summary>
    public class UserSettings
    {
        private const string Application = "mbremote";

        private const string PortNumber = "port";

        private const string Values = "values";

        private const string Selection = "selection";

        private const string NowPlayingLimit = "nplimit";

        private const string SFilename = "settings.xml";

        private const string SFolder = "mb_remote\\";

        private const string LastRunVersion = "lastrunversion";

        private static readonly UserSettings Settings = new UserSettings();

        private string storagePath;

        private string currentVersion;

        private uint listeningPort;

        private uint nowPlayingListLimit;

        private FilteringSelection filteringSelection;

        private string baseIp;

        private uint lastOctetMax;

        private List<String> ipAddressList;


        /// <summary>
        /// 
        /// </summary>
        public string StoragePath 
        {
            get
            {
                return storagePath;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public uint ListeningPort
        {
            get
            {
                return listeningPort;
            }
            set
            {
                listeningPort = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public uint NowPlayingListLimit
        {
            get
            {
                return nowPlayingListLimit > 0 ? nowPlayingListLimit : 5000;
            }
            set
            {
                nowPlayingListLimit = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public FilteringSelection FilterSelection
        {
            get
            {
                return filteringSelection;
            }
            set
            {
                filteringSelection = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public string BaseIp
        {
            get
            {
                return baseIp;
            }
            set
            {
                baseIp = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public uint LastOctetMax
        {
            get
            {
                return lastOctetMax;
            }
            set
            {
                lastOctetMax = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public List<string> IpAddressList
        {
            get
            {
                return ipAddressList;
            }
            set
            {
                ipAddressList = value;
            }
        }

        private UserSettings()
        {
            // Private constructor to enforce singleton
        }

        /// <summary>
        /// 
        /// </summary>
        public static UserSettings Instance
        {
            get
            {
                return Settings;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public string CurrentVersion
        {
            get
            {
                return currentVersion;
            }
            set
            {
                currentVersion = value;
            }
        }

	    /// <summary>
	    /// Since there is an issue with the existing search for a number of users
	    /// an alternative implementation exists.
	    /// </summary>
	    public bool AlternativeSearch { get; set; } = false;

	    /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        public void SetStoragePath(string path)
        {
            this.storagePath = path + SFolder;
        }

        private string GetSettingsFile()
        {
            return storagePath + SFilename;
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
                if (root != null)
                {
                    root.AppendChild(pattern);
                }
            }
            else
            {
                node.InnerText = value;
            }
        }

        /// <summary>
        /// Determines if it is the first run of the application.
        /// </summary>
        /// <returns></returns>
        public bool IsFirstRun()
        {
            bool isFirst = false;
            XmlDocument document = new XmlDocument();
            if (!File.Exists(GetSettingsFile()))
            {
                isFirst = true;
                CreateEmptySettingsFile(Application);
            }
            else
            {
                document.Load(GetSettingsFile());
                string lastRun = ReadNodeValue(document, LastRunVersion);

                if (String.IsNullOrEmpty(lastRun))
                {
                    isFirst = true;
                }    
            }

            if (isFirst)
            {
                document.Load(GetSettingsFile());
                WriteNodeValue(document, LastRunVersion, CurrentVersion);
                document.Save(GetSettingsFile());
            }
            
            return isFirst;
        }

        /// <summary>
        /// Saves the settings.
        /// </summary>
        /// <remarks></remarks>
        public void SaveSettings()
        {
            if (!File.Exists(GetSettingsFile()))
            {
                CreateEmptySettingsFile(Application);
            }
            XmlDocument document = new XmlDocument();
            document.Load(GetSettingsFile());
            WriteApplicationSetting(document);
            document.Save(GetSettingsFile());
            EventBus.FireEvent(new MessageEvent(EventType.RestartSocket));
        }

        private void CreateEmptySettingsFile(string application)
        {
            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
            }
            XmlDocument document = new XmlDocument();
            XmlDeclaration declaration = document.CreateXmlDeclaration("1.0", "utf-8", "yes");
            XmlElement root = document.CreateElement(application);
            document.InsertBefore(declaration, document.DocumentElement);
            document.AppendChild(root);
            document.Save(GetSettingsFile());
        }

        private void WriteApplicationSetting(XmlDocument document)
        {
            if (listeningPort < 65535)
            {
                WriteNodeValue(document, PortNumber, listeningPort.ToString(CultureInfo.InvariantCulture));
            }
            WriteNodeValue(document, Values, GetValues());
            WriteNodeValue(document, Selection, filteringSelection.ToString());
            WriteNodeValue(document, NowPlayingLimit, nowPlayingListLimit.ToString(CultureInfo.InvariantCulture));
        }

        private string ReadNodeValue(XmlNode document, string name)
        {
            XmlNode node = document.SelectSingleNode("//" + name);
            return node != null ? node.InnerText : string.Empty;
        }

        /// <summary>
        /// Loads the settings.
        /// </summary>
        /// <remarks></remarks>
        public void LoadSettings()
        {
            if (!File.Exists(GetSettingsFile()))
            {
                ListeningPort = 3000;
            }
            else
            {
                XmlDocument document = new XmlDocument();
                document.Load(GetSettingsFile());
                listeningPort = uint.TryParse(ReadNodeValue(document, PortNumber), out listeningPort)
                                                  ? listeningPort
                                                  : 3000;
                UpdateFilteringSelection(ReadNodeValue(document, Selection));
                SetValues(ReadNodeValue(document, Values));
                nowPlayingListLimit = uint.TryParse(ReadNodeValue(document, NowPlayingLimit), out nowPlayingListLimit)
                                          ? nowPlayingListLimit
                                          : 5000;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string GetValues()
        {
            switch (FilterSelection)
            {
                case FilteringSelection.All:
                    return String.Empty;
                case FilteringSelection.Range:
                    return FlattenAllowedRange();
                case FilteringSelection.Specific:
                    return FlattenAllowedAddressList();
                default:
                    return String.Empty;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="values"></param>
        public void SetValues(string values)
        {
            switch (filteringSelection)
            {
                case FilteringSelection.All:
                    break;
                case FilteringSelection.Range:
                    UnFlattenAllowedRange(values);
                    break;
                case FilteringSelection.Specific:
                    UnflattenAllowedAddressList(values);
                    break;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string FlattenAllowedRange()
        {
            if (!String.IsNullOrEmpty(baseIp) && (lastOctetMax > 1 || lastOctetMax < 255))
            {
                return baseIp + "," + LastOctetMax;
            }
            return String.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="range"></param>
        public void UnFlattenAllowedRange(string range)
        {
            if (String.IsNullOrEmpty(range))
            {
                return;
            }
            string[] splitRange = range.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            BaseIp = splitRange[0];
            LastOctetMax = uint.Parse(splitRange[1]);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string FlattenAllowedAddressList()
        {
            if (ipAddressList == null)
            {
                ipAddressList = new List<string>();
            }
            return ipAddressList.Aggregate<string, string>(null, (current, s) => current + (s + ","));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="selection"></param>
        public void UpdateFilteringSelection(string selection)
        {
            switch (selection)
            {
                case "All":
                    filteringSelection = FilteringSelection.All;
                    break;
                case "Range":
                    filteringSelection = FilteringSelection.Range;
                    break;
                case "Specific":
                    filteringSelection = FilteringSelection.Specific;
                    break;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="allowedAddresses"></param>
        public void UnflattenAllowedAddressList(string allowedAddresses)
        {
            ipAddressList =
                new List<string>(
                    allowedAddresses.Trim().Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries));
        }
    }
}