using System.IO;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     A thin in-memory reflection of the Rust-owned settings the host reads.
    ///     The core owns loading, migration, and persistence; <see cref="Host.PluginHost" />
    ///     sets the version and storage path once.
    /// </summary>
    public sealed class UserSettingsService : IUserSettings
    {
        private const string StorageSubFolder = "mb_remote";

        public string CurrentVersion { get; set; } = string.Empty;
        public string StoragePath { get; private set; } = string.Empty;

        public void SetStoragePath(string path) => StoragePath = Path.Combine(path, StorageSubFolder);
    }
}
