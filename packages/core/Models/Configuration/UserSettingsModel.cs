using System.Collections.Generic;
using MusicBeePlugin.Enumerations;

namespace MusicBeePlugin.Models.Configuration
{
    /// <summary>
    ///     Data model for user settings.
    ///     Contains only the settings data without any persistence logic.
    /// </summary>
    public class UserSettingsModel
    {
        public UserSettingsModel()
        {
            // Set default values
            ListeningPort = 3000;
            FilterSelection = FilteringSelection.All;
            BaseIp = string.Empty;
            LastOctetMax = 254;
            IpAddressList = new List<string>();
            Source = SearchSource.Library;
            AlternativeSearch = false;
            DebugLogEnabled = false;
            UpdateFirewall = false;
            CurrentVersion = string.Empty;
        }

        /// <summary>
        ///     The port number the socket server listens on.
        /// </summary>
        public uint ListeningPort { get; set; }

        /// <summary>
        ///     The IP address filtering mode.
        /// </summary>
        public FilteringSelection FilterSelection { get; set; }

        /// <summary>
        ///     Base IP address for range filtering (e.g., "192.168.1").
        /// </summary>
        public string BaseIp { get; set; }

        /// <summary>
        ///     Maximum value for the last octet when using range filtering.
        /// </summary>
        public uint LastOctetMax { get; set; }

        /// <summary>
        ///     List of allowed IP addresses when using specific filtering.
        /// </summary>
        public List<string> IpAddressList { get; set; }

        /// <summary>
        ///     The library source to use for searches.
        /// </summary>
        public SearchSource Source { get; set; }

        /// <summary>
        ///     Whether to use alternative search implementation.
        /// </summary>
        public bool AlternativeSearch { get; set; }

        /// <summary>
        ///     Whether debug logging is enabled.
        /// </summary>
        public bool DebugLogEnabled { get; set; }

        /// <summary>
        ///     Whether to update Windows firewall rules.
        /// </summary>
        public bool UpdateFirewall { get; set; }

        /// <summary>
        ///     The current plugin version.
        /// </summary>
        public string CurrentVersion { get; set; }

        /// <summary>
        ///     Creates a deep copy of the settings model.
        /// </summary>
        public UserSettingsModel Clone()
        {
            return new UserSettingsModel
            {
                ListeningPort = ListeningPort,
                FilterSelection = FilterSelection,
                BaseIp = BaseIp,
                LastOctetMax = LastOctetMax,
                IpAddressList = new List<string>(IpAddressList),
                Source = Source,
                AlternativeSearch = AlternativeSearch,
                DebugLogEnabled = DebugLogEnabled,
                UpdateFirewall = UpdateFirewall,
                CurrentVersion = CurrentVersion
            };
        }
    }
}
