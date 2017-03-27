using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Settings
{
    /// <summary>
    /// Represents the settings along with all the settings related functionality
    /// </summary>
    public class UserSettings
    {
        private const string Application = "mbremote";

        private const string PortNumber = "port";

        private const string Values = "values";

        private const string Selection = "selection";

        private const string SFilename = "settings.xml";

        private const string SFolder = "mb_remote\\";

        private const string LastRunVersion = "lastrunversion";

        private const string LibrarySource = "source";

        private const string LogsEnabled = "logs_enabled";

        private const string UpdateFirewallNode = "update_firewall";

        private uint _listeningPort;


        public SearchSource Source { get; set; }


        /// <summary>
        /// 
        /// </summary>
        public string StoragePath { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public uint ListeningPort
        {
            get { return _listeningPort; }
            set { _listeningPort = value; }
        }

        /// <summary>
        /// 
        /// </summary>
        public FilteringSelection FilterSelection { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string BaseIp { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public uint LastOctetMax { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public List<string> IpAddressList { get; set; }

        public UserSettings(ITinyMessengerHub hub)
        {
            _hub = hub;
        }

        /// <summary>
        /// 
        /// </summary>
        public string CurrentVersion { get; set; }

        /// <summary>
        /// Since there is an issue with the existing search for a number of users
        /// an alternative implementation exists.
        /// </summary>
        public bool AlternativeSearch { get; set; } = false;

        /// <summary>
        /// Enables Debug logging to the production version of the plugin
        /// </summary>
        public bool DebugLogEnabled { get; set; }

        public static string LogFilePath = "\\mbrc.log";
        private ITinyMessengerHub _hub;

        public string FullLogPath => StoragePath + LogFilePath;
        public bool UpdateFirewall { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        public void SetStoragePath(string path)
        {
            StoragePath = path + SFolder;
        }

        private string GetSettingsFile()
        {
            return StoragePath + SFilename;
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
            var node = document.SelectSingleNode("//" + name);
            if (node == null)
            {
                var pattern = document.CreateElement(name);
                XmlNode root = document.DocumentElement;
                pattern.InnerText = value;
                root?.AppendChild(pattern);
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
            var isFirst = false;
            var document = new XmlDocument();
            if (!File.Exists(GetSettingsFile()))
            {
                isFirst = true;
                CreateEmptySettingsFile(Application);
            }
            else
            {
                document.Load(GetSettingsFile());
                var lastRun = ReadNodeValue(document, LastRunVersion);

                if (string.IsNullOrEmpty(lastRun))
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
            var document = new XmlDocument();
            document.Load(GetSettingsFile());
            WriteApplicationSetting(document);
            document.Save(GetSettingsFile());
            _hub.Publish(new RestartSocketEvent());
        }

        private void CreateEmptySettingsFile(string application)
        {
            if (!Directory.Exists(StoragePath))
            {
                Directory.CreateDirectory(StoragePath);
            }
            var document = new XmlDocument();
            var declaration = document.CreateXmlDeclaration("1.0", "utf-8", "yes");
            var root = document.CreateElement(application);
            document.InsertBefore(declaration, document.DocumentElement);
            document.AppendChild(root);
            document.Save(GetSettingsFile());
        }

        private void WriteApplicationSetting(XmlDocument document)
        {
            if (_listeningPort < 65535)
            {
                WriteNodeValue(document, PortNumber, _listeningPort.ToString(CultureInfo.InvariantCulture));
            }
            WriteNodeValue(document, Values, GetValues());
            WriteNodeValue(document, Selection, FilterSelection.ToString());
            WriteNodeValue(document, LibrarySource, ((short) Source).ToString());
            WriteNodeValue(document, LogsEnabled, DebugLogEnabled.ToString());
            WriteNodeValue(document, UpdateFirewallNode, UpdateFirewall.ToString());
        }

        private static string ReadNodeValue(XmlNode document, string name)
        {
            var node = document.SelectSingleNode("//" + name);
            return node?.InnerText ?? string.Empty;
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
                var document = new XmlDocument();
                document.Load(GetSettingsFile());
                _listeningPort = uint.TryParse(ReadNodeValue(document, PortNumber), out _listeningPort)
                    ? _listeningPort
                    : 3000;
                UpdateFilteringSelection(ReadNodeValue(document, Selection));
                SetValues(ReadNodeValue(document, Values));
                bool debugEnabled;
                bool.TryParse(ReadNodeValue(document, LogsEnabled), out debugEnabled);
                DebugLogEnabled = debugEnabled;

                bool updateFirewall;
                bool.TryParse(ReadNodeValue(document, UpdateFirewallNode), out updateFirewall);
                UpdateFirewall = updateFirewall;


                short source;
                Source =
                    (SearchSource) (short.TryParse(ReadNodeValue(document, LibrarySource), out source) ? source : 1);
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
                    return string.Empty;
                case FilteringSelection.Range:
                    return FlattenAllowedRange();
                case FilteringSelection.Specific:
                    return FlattenAllowedAddressList();
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="values"></param>
        public void SetValues(string values)
        {
            switch (FilterSelection)
            {
                case FilteringSelection.All:
                    break;
                case FilteringSelection.Range:
                    UnFlattenAllowedRange(values);
                    break;
                case FilteringSelection.Specific:
                    UnflattenAllowedAddressList(values);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string FlattenAllowedRange()
        {
            if (!string.IsNullOrEmpty(BaseIp) && (LastOctetMax > 1 || LastOctetMax < 255))
            {
                return BaseIp + "," + LastOctetMax;
            }
            return string.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="range"></param>
        public void UnFlattenAllowedRange(string range)
        {
            if (string.IsNullOrEmpty(range))
            {
                return;
            }
            var splitRange = range.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            BaseIp = splitRange[0];
            LastOctetMax = uint.Parse(splitRange[1]);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string FlattenAllowedAddressList()
        {
            if (IpAddressList == null)
            {
                IpAddressList = new List<string>();
            }
            return IpAddressList.Aggregate<string, string>(null, (current, s) => current + (s + ","));
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
                    FilterSelection = FilteringSelection.All;
                    break;
                case "Range":
                    FilterSelection = FilteringSelection.Range;
                    break;
                case "Specific":
                    FilterSelection = FilteringSelection.Specific;
                    break;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="allowedAddresses"></param>
        public void UnflattenAllowedAddressList(string allowedAddresses)
        {
            IpAddressList =
                new List<string>(
                    allowedAddresses.Trim().Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries));
        }
    }
}