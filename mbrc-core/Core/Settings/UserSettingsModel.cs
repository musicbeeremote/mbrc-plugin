using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Settings
{
    [DataContract]
    public class UserSettingsModel
    {
        /// <summary>
        /// Gets or sets the listening port for the incoming connections of the socket server.
        /// </summary>
        [DataMember(Name = "port")]
        public uint ListeningPort { get; set; } = 3000;

        /// <summary>
        /// Gets or sets the IP address filtering selection. By default all connections are allowed but the user can select
        /// to allow only some connection while blocking others.
        /// </summary>
        [DataMember(Name = "filtering")]
        public FilteringSelection FilterSelection { get; set; } = FilteringSelection.All;

        /// <summary>
        /// Gets or sets the starting address of an IPv4 range that will be allowed to connect to the plugin.
        /// <see cref="FilteringSelection.Range"/> filtering to indicate.
        /// <example>192.168.1.10</example>
        /// </summary>
        [DataMember(Name = "base_ip")]
        public string BaseIp { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the last octet of the IPv4 address.
        /// The allowed addresses range will start with <see cref="BaseIp"/> and will end to the digit indicated
        /// by the last octet. Used with <see cref="FilteringSelection.Range"/> filtering.
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
        /// Gets or sets the IPv4 addresses contained withing this list will be allowed to connect to the plugin.
        /// Used with <see cref="FilteringSelection.Specific"/>.
        /// </summary>
        [DataMember(Name = "specific_ips")]
        public List<string> IpAddressList { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the current version of the plugin.
        /// </summary>
        [DataMember(Name = "plugin_version")]
        public string CurrentVersion { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the plugin should log into a file or not.
        /// Under normal circumstances this should be deactivated.
        /// </summary>
        [DataMember(Name = "debug_logs")]
        public bool DebugLogEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the firewall should be automatically updated via the helper utility or not.
        /// </summary>
        [DataMember(Name = "update_firewall")]
        public bool UpdateFirewall { get; set; }
    }
}
