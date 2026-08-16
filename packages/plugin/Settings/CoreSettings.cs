using System.Collections.Generic;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     A read-only C# reflection of the Rust-owned settings
    ///     (<c>core_settings.json</c>), obtained via <c>mbrc_read_settings</c>. The
    ///     Rust core is the single source of truth; this snapshot is refreshed at
    ///     init and after an apply, and the host reads settings from it instead of
    ///     keeping a parallel store. Property names are the JSON keys.
    /// </summary>
    public sealed class CoreSettings
    {
        public int port { get; set; } = 3000;

        /// <summary>Address filter mode: <c>all</c> / <c>range</c> / <c>specific</c>.</summary>
        public string filter_mode { get; set; } = "all";

        /// <summary>Range mode: base IPv4 (its last octet is the low bound).</summary>
        public string base_ip { get; set; } = "";

        /// <summary>Range mode: inclusive high end of the allowed last octet.</summary>
        public int last_octet_max { get; set; } = 254;

        /// <summary>Specific mode: exact addresses and/or CIDR blocks allowed.</summary>
        public List<string> allowed_addresses { get; set; } = new List<string>();

        /// <summary>MusicBee <c>SearchSource</c> flags value (Library=1, ...).</summary>
        public int search_source { get; set; } = 1;

        /// <summary>Whether the host adds a Windows firewall rule on save.</summary>
        public bool update_firewall { get; set; }

        /// <summary>Core log verbosity: <c>info</c> / <c>debug</c> / <c>trace</c>.</summary>
        public string log_level { get; set; } = "info";

        /// <summary>
        ///     Whether the plugin also advertises itself over mDNS / DNS-SD
        ///     (Bonjour). Additive to the custom UDP discovery, which is
        ///     unaffected either way, so this is safe to turn off.
        /// </summary>
        public bool mdns_enabled { get; set; } = true;

        /// <summary>
        ///     Whether the core checks for a newer release on its own. Off by
        ///     default: an automatic check is an unprompted request to github.com.
        ///     The panel's "Check now" button works regardless of this.
        /// </summary>
        public bool update_check_enabled { get; set; }
    }
}
