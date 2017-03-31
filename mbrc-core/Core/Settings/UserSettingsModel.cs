using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeeRemoteCore.Core.Settings
{
    [DataContract]
    public class UserSettingsModel
    {
        /// <summary>
        /// The listening port for the incoming connections of the socket server.
        /// </summary>
        [DataMember(Name = "port")]
        public uint ListeningPort { get; set; } = 3000;

        /// <summary>
        /// The IP address filtering selection. By default all connections are allowed but the user can select
        /// to allow only some connection while blocking others.
        /// </summary>
        [DataMember(Name = "filtering")]
        public FilteringSelection FilterSelection { get; set; } = FilteringSelection.All;

        /// <summary>
        /// Used with <see cref="FilteringSelection.Range"/> filtering to indicate the starting address of an IPv4
        /// range that will be allowed to connect to the plugin.
        /// <example>192.168.1.10</example>
        /// </summary>
        [DataMember(Name = "base_ip")]
        public string BaseIp { get; set; } = "";

        /// <summary>
        /// Used with <see cref="FilteringSelection.Range"/> filtering to indicate the last octet of the IPv4 address.
        /// The allowed addresses range will start with <see cref="BaseIp"/> and will end to the digit indicated
        /// by the last octet.
        /// <example>
        /// BaseIp = 192.168.1.10
        /// LastOctetMax = 20
        ///
        /// This means that the only the addresses from 192.168.1.10 -> 192.168.1.20 will be allowed to connect.
        /// </example>
        /// </summary>
        [DataMember(Name = "last_octet")]
        public uint LastOctetMax { get; set; }

        /// <summary>
        /// Used with <see cref="FilteringSelection.Specific"/>. When the specific filtering mode is active
        /// only the IPv4 addresses contained withing this list will be allowed to connect to the plugin.
        /// </summary>
        [DataMember(Name = "specific_ips")]
        public List<string> IpAddressList { get; set; } = new List<string>();

        /// <summary>
        /// The current version of the plugin. Peristed and saved in order to ensure that the <see cref="InfoWindow"/>
        /// is shown once after each update to the user.
        /// </summary>
        [DataMember(Name = "plugin_version")]
        public string CurrentVersion { get; set; } = "";

        /// <summary>
        /// Since there is an issue with the existing search for a number of users
        /// an alternative implementation exists.
        /// </summary>
        [DataMember(Name = "alternative_search_enabled")]
        public bool AlternativeSearch { get; set; }

        /// <summary>
        /// If this value is true then the fully verbose debug logging is active while on the release build.
        /// Under normal circumstances this should be deactivated.
        /// </summary>
        [DataMember(Name = "debug_logs")]
        public bool DebugLogEnabled { get; set; }

        /// <summary>
        /// If this option is selected then the when saving the plugin settings the application will attempt
        /// to start the bundled firewall utility in order to update the user's firewall rules automatically.
        /// </summary>
        [DataMember(Name = "update_firewall")]
        public bool UpdateFirewall { get; set; }
    }
}