using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Models;
using MusicBeePlugin.Logging;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Settings;

namespace MusicBeePlugin.Host
{
    /// <summary>
    ///     Hand-wired composition root for the Rust-backed plugin: it <c>new</c>s
    ///     the MusicBee-bound providers, the settings read-cache, and the
    ///     <see cref="NativeBridge"/>, then boots the Rust core. No DI container,
    ///     no C# server, no C# cover cache; logging routes through the core's
    ///     <see cref="FfiLogger"/>.
    /// </summary>
    public sealed class PluginHost : IDisposable
    {
        private readonly NativeBridge _bridge;
        private readonly UserSettingsService _userSettings;
        private readonly ISystemOperations _system;
        private readonly IPluginLogger _logger;
        private bool _disposed;

        public PluginHost(Plugin.MusicBeeApiInterface api, string storagePath, Version version)
        {
            // All C# logs route through the Rust core's logger (one log file).
            // Pre-init logs fall back to mbrc-bootstrap.log in the same storage
            // folder the core writes mbrc-core.log to (<storage>/mb_remote).
            var storageDir = Path.Combine(storagePath, "mb_remote");
            _logger = new FfiLogger("mbrc.host", storageDir);

            // MusicBee-API-bound leaves: providers wrap the raw interface struct.
            _system = new SystemOperations(api);
            var player = new PlayerDataProvider(api);
            var track = new TrackDataProvider(api);
            var playlist = new PlaylistDataProvider(api);
            var library = new LibraryDataProvider(api);

            // Settings are Rust-owned; C# caches only the few the host reads
            // (search source, version, storage path), refreshed from the core
            // after init. The core migrates settings.xml and owns the JSON store.
            var settings = new UserSettingsService { CurrentVersion = version.ToString() };
            settings.SetStoragePath(storagePath);
            _userSettings = settings;

            // The FFI boundary to the Rust core. Cover caching now lives entirely
            // in the Rust core (it builds + serves from core_settings storage);
            // there is no C# cover service.
            _bridge = new NativeBridge(player, track, playlist, library, settings, _system, _logger);
        }

        /// <summary>
        ///     Boots the Rust core and starts serving. Never throws: the bridge
        ///     degrades (remote disabled) rather than crashing MusicBee.
        /// </summary>
        public void Start()
        {
            _bridge.Initialize(_userSettings.StoragePath);

            // Rust owns settings: pull the core's config into the C# read-cache so
            // the search source etc. mirror it, and set the core's log level to
            // match the persisted debug flag.
            var settings = _bridge.ReadSettings() ?? new CoreSettings();
            SyncSettingsCache(settings);
            _bridge.SetLogLevel(settings.log_level);

            _bridge.StartNetworking();

            // The Rust core owns the album cover cache end to end: it warms up and
            // builds it when networking starts and serves every cover query. The
            // host only provides raw MusicBee ingredients through the FFI.
        }

        /// <summary>Forward a MusicBee notification (already mapped to 0-7) to the core.</summary>
        public void HandleNotification(int notificationType) => _bridge.HandleNotification(notificationType);

        /// <summary>
        ///     The folder holding the log files (<c>mbrc-core.log</c> +
        ///     <c>mbrc-bootstrap.log</c>), for the settings panel's "Open log
        ///     folder" button. Already the <c>mb_remote</c> storage subfolder.
        /// </summary>
        public string LogDirectory => _userSettings.StoragePath;

        /// <summary>The core's current settings, for the configuration panel.</summary>
        public CoreSettings ReadSettings() => _bridge.ReadSettings();

        /// <summary>The core's cache status, for the configuration panel's cache line.</summary>
        public CoreCacheStatus ReadCacheStatus() => _bridge.ReadCacheStatus();

        /// <summary>Trigger a background rebuild of the metadata (browse) cache.</summary>
        public bool RebuildMetadata() => _bridge.RebuildMetadata();

        /// <summary>Trigger a background rebuild of the cover cache.</summary>
        public bool RebuildCovers() => _bridge.RebuildCovers();

        /// <summary>
        ///     Recent rejected connection attempts (newest first) for the settings
        ///     panel's blocked-connections view. Never null.
        /// </summary>
        public List<BlockedConnection> ReadBlockedConnections() => _bridge.ReadBlockedConnections();

        /// <summary>Clear the core's in-memory blocked-connection log.</summary>
        public bool ClearBlockedConnections() => _bridge.ClearBlockedConnections();

        /// <summary>
        ///     Core -> host push events (raised on a background thread). The
        ///     settings panel subscribes to refresh its cache line when a rebuild
        ///     starts/finishes. Forwarded from the native bridge.
        /// </summary>
        public event Action<Ffi.Generated.HostEventType> CoreEvent
        {
            add => _bridge.CoreEvent += value;
            remove => _bridge.CoreEvent -= value;
        }

        /// <summary>
        ///     Apply settings edited in the configuration panel. Persists them to
        ///     the Rust core (which validates), refreshes the C# read-cache, and
        ///     does only the work each change needs: debug logging live, the
        ///     firewall rule host-side, and a listener reload only when the port or
        ///     address filter changed. Returns false if the core rejected them.
        /// </summary>
        public bool ApplySettings(CoreSettings updated)
        {
            if (updated == null) return false;

            var current = _bridge.ReadSettings() ?? new CoreSettings();
            if (!_bridge.WriteSettings(updated))
                return false; // rejected (e.g. bad port); nothing changed

            // Reflect the new values in the C# read-cache (search source, etc.).
            SyncSettingsCache(updated);

            // The log level is applied live - no restart needed.
            if (!string.Equals(updated.log_level, current.log_level, StringComparison.OrdinalIgnoreCase))
                _bridge.SetLogLevel(updated.log_level);

            // Add/refresh the Windows firewall rule if the user opted in.
            if (updated.update_firewall)
                UpdateFirewallRule(updated.port);

            // Only a port or address-filter change needs the listener rebound.
            if (NeedsRestart(current, updated))
                _bridge.Reload();

            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bridge.StopNetworking();
            _bridge.Dispose();
        }

        /// <summary>
        ///     Mirror the Rust-owned settings into the C# read-cache so the parts
        ///     the host still reads (search source, alternative search) reflect the
        ///     core. CurrentVersion is preserved (set at construction).
        /// </summary>
        private void SyncSettingsCache(CoreSettings s)
        {
            // Only the search source is consumed at runtime; the rest of the Rust
            // config is edited/read through the panel (CoreSettings), not here.
            _userSettings.Source = (SearchSource)s.search_source;
        }

        /// <summary>Only a port or address-filter change needs the listener rebound.</summary>
        private static bool NeedsRestart(CoreSettings a, CoreSettings b)
        {
            return a.port != b.port
                   || a.filter_mode != b.filter_mode
                   || a.base_ip != b.base_ip
                   || a.last_octet_max != b.last_octet_max
                   || !ListEqual(a.allowed_addresses, b.allowed_addresses);
        }

        private static bool ListEqual(List<string> a, List<string> b)
        {
            a = a ?? new List<string>();
            b = b ?? new List<string>();
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        /// <summary>
        ///     Run the bundled firewall-utility (elevated) to add/refresh the
        ///     inbound rule for the listening port. Best-effort - a missing helper
        ///     or a declined UAC prompt is logged, never fatal.
        /// </summary>
        private void UpdateFirewallRule(int port)
        {
            try
            {
                var cmd = $"{AppDomain.CurrentDomain.BaseDirectory}\\Plugins\\firewall-utility.exe";
                if (!File.Exists(cmd))
                {
                    _logger.Warn("firewall-utility.exe not found; skipping firewall rule");
                    return;
                }

                Process.Start(new ProcessStartInfo(cmd)
                {
                    Verb = "runas",
                    Arguments = $"-s {port}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update firewall rule");
            }
        }
    }
}
