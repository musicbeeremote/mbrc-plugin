using System.Collections.Generic;
using MusicBeePlugin.Enumerations;

namespace MusicBeePlugin.Services.Configuration
{
    /// <summary>
    ///     Interface for user settings data access.
    ///     Provides read-only access to the current settings.
    /// </summary>
    public interface IUserSettings
    {
        /// <summary>
        ///     The port number the socket server listens on.
        /// </summary>
        uint ListeningPort { get; }

        /// <summary>
        ///     The IP address filtering mode.
        /// </summary>
        FilteringSelection FilterSelection { get; }

        /// <summary>
        ///     Base IP address for range filtering (e.g., "192.168.1").
        /// </summary>
        string BaseIp { get; }

        /// <summary>
        ///     Maximum value for the last octet when using range filtering.
        /// </summary>
        uint LastOctetMax { get; }

        /// <summary>
        ///     List of allowed IP addresses when using specific filtering.
        /// </summary>
        IReadOnlyList<string> IpAddressList { get; }

        /// <summary>
        ///     The library source to use for searches.
        /// </summary>
        SearchSource Source { get; }

        /// <summary>
        ///     Whether to use alternative search implementation.
        /// </summary>
        bool AlternativeSearch { get; }

        /// <summary>
        ///     Whether debug logging is enabled.
        /// </summary>
        bool DebugLogEnabled { get; }

        /// <summary>
        ///     Whether to update Windows firewall rules.
        /// </summary>
        bool UpdateFirewall { get; }

        /// <summary>
        ///     The current plugin version.
        /// </summary>
        string CurrentVersion { get; }

        /// <summary>
        ///     The full path to the storage directory.
        /// </summary>
        string StoragePath { get; }

        /// <summary>
        ///     The full path to the log file.
        /// </summary>
        string FullLogPath { get; }
    }
}
