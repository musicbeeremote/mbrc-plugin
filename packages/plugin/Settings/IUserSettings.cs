namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The slice of settings the host reads at runtime. Settings are
    ///     Rust-owned; the host reads only the plugin version (the pluginversion
    ///     query) and the storage path (init).
    /// </summary>
    public interface IUserSettings
    {
        /// <summary>The plugin version string (served for the pluginversion query).</summary>
        string CurrentVersion { get; }

        /// <summary>The plugin's storage directory (&lt;persistent&gt;/mb_remote).</summary>
        string StoragePath { get; }
    }
}
