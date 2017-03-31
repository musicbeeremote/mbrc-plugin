using System;
using System.IO;
using System.Linq;
using System.Xml;

namespace MusicBeeRemoteCore.Core.Settings
{
    class LegacySettingsMigration : ILegacySettingsMigration
    {
        private readonly IJsonSettingsFileManager _jsonSettingsFileManager;
        private readonly string _settingsFile;

        private const string PortTag = "port";
        private const string Values = "values";
        private const string SelectionTag = "selection";
        private const string LogsEnabledTag = "logs_enabled";
        private const string UpdateFirewallTag = "update_firewall";

        public LegacySettingsMigration(IStorageLocationProvider storageLocationProvider,
            IJsonSettingsFileManager jsonSettingsFileManager)
        {
            _jsonSettingsFileManager = jsonSettingsFileManager;
            _settingsFile = storageLocationProvider.LegacySettingsFile;
        }

        public bool MigrateLegacySettings(UserSettingsModel model)
        {
            if (!File.Exists(_settingsFile))
            {
                return false;
            }
            LoadSettings(model, _settingsFile);
            _jsonSettingsFileManager.Save(model);

            File.Delete(_settingsFile);
            return true;
        }

        private static string ReadNodeValue(XmlNode document, string name)
        {
            var node = document.SelectSingleNode("//" + name);
            return node?.InnerText ?? string.Empty;
        }


        private void LoadSettings(UserSettingsModel model, string settingsFile)
        {
            var document = new XmlDocument();
            document.Load(settingsFile);
            uint portNumber;
            if (!uint.TryParse(ReadNodeValue(document, PortTag), out portNumber))
            {
                portNumber = 3000;
            }
            model.ListeningPort = portNumber;

            UpdateFilteringSelection(model, ReadNodeValue(document, SelectionTag));
            SetValues(model, ReadNodeValue(document, Values));

            model.DebugLogEnabled = ReadBoolTag(LogsEnabledTag, document);
            model.UpdateFirewall = ReadBoolTag(UpdateFirewallTag, document);
        }

        private static bool ReadBoolTag(string tag, XmlNode document)
        {
            bool output;
            return bool.TryParse(ReadNodeValue(document, tag), out output) && output;
        }


        /// <summary>
        /// </summary>
        /// <param name="model"></param>
        /// <param name="values"></param>
        public void SetValues(UserSettingsModel model, string values)
        {
            switch (model.FilterSelection)
            {
                case FilteringSelection.All:
                    break;
                case FilteringSelection.Range:
                    UnFlattenAllowedRange(model, values);
                    break;
                case FilteringSelection.Specific:
                    UnflattenAllowedAddressList(model, values);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="model"></param>
        /// <param name="range"></param>
        public void UnFlattenAllowedRange(UserSettingsModel model, string range)
        {
            if (string.IsNullOrEmpty(range))
            {
                return;
            }
            var splitRange = range.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            model.BaseIp = splitRange[0];
            model.LastOctetMax = uint.Parse(splitRange[1]);
        }

        /// <summary>
        /// Reads the filter selection value from the xml file and update the UserSettingsModel.
        /// </summary>
        /// <param name="model">The model that stores all the user perferences.</param>
        /// <param name="selection">The selection value stored in the xml</param>
        public void UpdateFilteringSelection(UserSettingsModel model, string selection)
        {
            switch (selection)
            {
                case "All":
                    model.FilterSelection = FilteringSelection.All;
                    break;
                case "Range":
                    model.FilterSelection = FilteringSelection.Range;
                    break;
                case "Specific":
                    model.FilterSelection = FilteringSelection.Specific;
                    break;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="model"></param>
        /// <param name="allowedAddresses"></param>
        public void UnflattenAllowedAddressList(UserSettingsModel model, string allowedAddresses)
        {
            model.IpAddressList = allowedAddresses.Trim()
                .Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }
    }
}