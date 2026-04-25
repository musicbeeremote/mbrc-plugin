using System;
using System.IO;
using System.Reflection;
using MusicBeePlugin.Adapters.Implementations;
using MusicBeePlugin.Core;
using MusicBeePlugin.DataProviders;
using MusicBeePlugin.Events.Messages;
using MusicBeePlugin.Services;
using MusicBeePlugin.Services.Core;

namespace MusicBeePlugin
{
    /// <summary>
    ///     The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    public partial class Plugin
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private readonly PluginInfo _about = new PluginInfo();

        /// <summary>
        ///     The mb api interface.
        /// </summary>
        private MusicBeeApiInterface _api;

        /// <summary>
        ///     The plugin core for dependency injection.
        /// </summary>
        private Core.PluginCore _pluginCore;

        /// <summary>
        ///     Native bridge to the Rust HTTP server.
        /// </summary>
        private NativeBridge _nativeBridge;

        /// <summary>
        ///     This function initialized the Plugin.
        /// </summary>
        /// <param name="apiInterfacePtr"></param>
        /// <returns></returns>
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _api = new MusicBeeApiInterface();
            _api.Initialise(apiInterfacePtr);

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "MusicBee Remote: Plugin";
            _about.Description = "Remote Control for server to be used with android application.";
            _about.Author = "Konstantinos Paparas (aka Kelsos)";
            _about.TargetApplication = "MusicBee Remote";
            _about.Type = PluginType.General;
            _about.VersionMajor = Convert.ToInt16(version.Major);
            _about.VersionMinor = Convert.ToInt16(version.Minor);
            _about.Revision = Convert.ToInt16(version.Build);
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;

            if (_api.ApiRevision < MinApiRevision)
                return _about;

            InitializePluginCore(version);

            return _about;
        }

        /// <summary>
        ///     Initializes the plugin core with dependency injection, networking, and menu items.
        ///     Wraps initialization in try-catch for robust error handling.
        /// </summary>
        /// <param name="version">The plugin version from assembly.</param>
        private void InitializePluginCore(Version version)
        {
            try
            {
                // Create adapter composition for dependency injection (system operations only)
                var adapters = new MusicBeeApiAdapter(
                    new SystemOperations(_api)
                );

                // Create data providers composition for dependency injection
                var dataProviders = new DataProviders.DataProviders(
                    new PlayerDataProvider(_api),
                    new TrackDataProvider(_api),
                    new PlaylistDataProvider(_api),
                    new LibraryDataProvider(_api)
                );

                // Initialize plugin core with dependency injection
                _pluginCore = new PluginCore(
                    adapters,
                    dataProviders,
                    _api.Setting_GetPersistentStoragePath(),
                    version
                );

                // Initialize core services. The legacy C# socket server is no
                // longer started — Rust owns networking. Settings, cover
                // service, notifications etc. still come up via PluginCore so
                // the Rust callbacks can resolve their data providers.
                _pluginCore.Initialize();

                // Initialize Rust core with data providers for callbacks
                _nativeBridge = new NativeBridge(
                    dataProviders.Player,
                    dataProviders.Track,
                    dataProviders.Playlist,
                    dataProviders.Library,
                    _pluginCore.UserSettings,
                    _pluginCore.CoverService);
                _nativeBridge.Initialize(_api.Setting_GetPersistentStoragePath());
                _pluginCore.RegisterNativeBridge(_nativeBridge);
                _nativeBridge.StartNetworking();

                // Restart the Rust server when the user changes settings —
                // re-reads core_settings.json. The old MessageSendEvent-based
                // SocketRestart trigger fed the C# SocketServer which is now
                // gone; subscribe directly so no protocol-layer event is
                // needed for a runtime concern.
                //
                // Runs on a background thread so the WinForms save handler
                // doesn't block. The 150 ms gap between stop and start is
                // there because mbrc_stop_networking only *signals* the
                // shutdown — the listener task tears down asynchronously,
                // and a back-to-back StartNetworking can race the OS port
                // release. 150 ms is empirically enough on Windows for the
                // TIME_WAIT-free SO_REUSEADDR rebind we don't yet do.
                _pluginCore.EventAggregator.Subscribe<CoreRestartRequestedEvent>(_ =>
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            _nativeBridge?.StopNetworking();
                            System.Threading.Thread.Sleep(150);
                            _nativeBridge?.StartNetworking();
                        }
                        catch
                        {
                            // Restart failures must never crash MusicBee.
                            // The user can re-open settings and retry.
                        }
                    });
                });

                // Add MusicBee menu item for settings
                _api.MB_AddMenuItem(
                    "mnuTools/MusicBee Remote",
                    "Information Panel of the MusicBee Remote",
                    (sender, args) => _pluginCore?.OpenSettingsWindow()
                );
            }
            catch (Exception ex)
            {
                // Log the error to fallback location and ensure plugin doesn't crash MusicBee
                try
                {
                    var fallbackPath = Path.Combine(
                        _api.Setting_GetPersistentStoragePath(),
                        "mb_remote",
                        "initialization_error.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath));
                    File.AppendAllText(fallbackPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL: Plugin initialization failed: {ex}\n");
                }
                catch
                {
                    // If logging fails, continue silently to prevent MusicBee crash
                }

                // Ensure _pluginCore is null so other methods can handle gracefully
                _pluginCore = null;
            }
        }

        /// <summary>
        ///     Creates the MusicBee plugin Configuration panel.
        /// </summary>
        /// <param name="panelHandle">
        ///     The panel handle.
        /// </param>
        /// <returns>
        ///     The <see cref="bool" />.
        /// </returns>
        public bool Configure(IntPtr panelHandle)
        {
            _pluginCore?.OpenSettingsWindow();
            return true;
        }

        /// <summary>
        ///     Called when MusicBee closes or the plugin is disabled.
        /// </summary>
        /// <param name="reason">The reason for closing.</param>
        public void Close(PluginCloseReason reason)
        {
            _nativeBridge?.Dispose();
            _nativeBridge = null;

            _pluginCore?.Dispose();
            _pluginCore = null;
        }

        /// <summary>
        ///     Cleans up any persisted files during the plugin uninstall.
        /// </summary>
        public void Uninstall()
        {
            _pluginCore?.Uninstall();
        }

        /// <summary>
        ///     Called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        ///     Settings are now saved through the InfoWindow's UserSettingsService when changes are applied.
        ///     This method is kept for MusicBee API compatibility.
        /// </summary>
        public static void SaveSettings()
        {
            // Settings are saved via UserSettingsService when the user applies changes in InfoWindow.
            // This empty implementation maintains compatibility with MusicBee's plugin interface.
        }

        /// <summary>
        ///     Receives event Notifications from MusicBee. It is only required if the about.ReceiveNotificationFlags =
        ///     PlayerEvents.
        /// </summary>
        /// <param name="sourceFileUrl"></param>
        /// <param name="type"></param>
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            // Forward to Rust — it handles broadcast fan-out for all
            // protocol-visible notifications.
            ForwardNotificationToRust(type);

            // The only C#-side responsibility left is priming the cover
            // cache when a new file is added to the library; the cache
            // itself stays C#-owned for this release (per rollout plan).
            if (type == NotificationType.FileAddedToLibrary && _pluginCore != null)
            {
                try
                {
                    _pluginCore.CoverService.CacheTrackCover(sourceFileUrl);
                }
                catch
                {
                    // Cover cache failures must never crash MusicBee.
                }
            }
        }

        /// <summary>
        ///     Maps MusicBee NotificationType to Rust NotificationType int values
        ///     and forwards to the native bridge.
        /// </summary>
        private void ForwardNotificationToRust(NotificationType type)
        {
            int rustType;
            switch (type)
            {
                case NotificationType.TrackChanged:
                    rustType = 0;
                    break;
                case NotificationType.PlayStateChanged:
                    rustType = 1;
                    break;
                case NotificationType.VolumeLevelChanged:
                    rustType = 2;
                    break;
                case NotificationType.VolumeMuteChanged:
                    rustType = 3;
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    rustType = 4;
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    rustType = 5;
                    break;
                case NotificationType.PlayingTracksChanged:
                    rustType = 6;
                    break;
                case NotificationType.FileAddedToLibrary:
                    rustType = 7;
                    break;
                default:
                    return;
            }

            _nativeBridge?.HandleNotification(rustType);
        }
    }
}
