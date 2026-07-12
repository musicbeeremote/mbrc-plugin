using System.IO;
using MusicBeePlugin.Models;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     A thin in-memory reflection of the Rust-owned settings the host reads.
    ///     The core owns loading, migration, and persistence; <see cref="Host.PluginHost" />
    ///     sets the version + storage path once and refreshes <see cref="Source" />
    ///     from the core after each apply.
    /// </summary>
    public sealed class UserSettingsService : IUserSettings
    {
        private const string StorageSubFolder = "mb_remote";

        // Source is read on FFI query threads and written on init/apply; an enum
        // read/write is atomic on the CLR (a stale read right after an apply is
        // harmless), so no lock is needed.
        public SearchSource Source { get; set; } = SearchSource.Library;
        public string CurrentVersion { get; set; } = string.Empty;
        public string StoragePath { get; private set; } = string.Empty;

        public void SetStoragePath(string path) => StoragePath = Path.Combine(path, StorageSubFolder);
    }
}
