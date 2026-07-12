using MusicBeePlugin.Models;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The slice of settings the host reads at runtime. Settings are
    ///     Rust-owned; this is a thin cache refreshed from the core. Only these
    ///     three are actually consumed - the search source (library queries), the
    ///     plugin version (the pluginversion query), and the storage path (init).
    /// </summary>
    public interface IUserSettings
    {
        /// <summary>Which MusicBee sources library search/browse targets.</summary>
        SearchSource Source { get; }

        /// <summary>The plugin version string (served for the pluginversion query).</summary>
        string CurrentVersion { get; }

        /// <summary>The plugin's storage directory (&lt;persistent&gt;/mb_remote).</summary>
        string StoragePath { get; }
    }
}
