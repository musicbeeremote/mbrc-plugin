using System;
using System.IO;
using System.Reflection;
using MusicBeePlugin.Adapters.Implementations;
using MusicBeePlugin.Core;
using MusicBeePlugin.DataProviders;
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

                // Initialize core services and networking
                _pluginCore.Initialize();
                _pluginCore.StartNetworking();

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
            _pluginCore?.StopNetworking();
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
            // Perform an action depending on the notification type
            var notificationHandler = _pluginCore?.GetNotificationHandler();
            if (notificationHandler == null)
                return;

            switch (type)
            {
                case NotificationType.TrackChanged:
                    notificationHandler.HandleTrackChanged(sourceFileUrl);
                    break;
                case NotificationType.VolumeLevelChanged:
                    notificationHandler.HandleVolumeLevelChanged();
                    break;
                case NotificationType.VolumeMuteChanged:
                    notificationHandler.HandleVolumeMuteChanged();
                    break;
                case NotificationType.PlayStateChanged:
                    notificationHandler.HandlePlayStateChanged();
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    notificationHandler.HandleNowPlayingLyricsReady();
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    notificationHandler.HandleNowPlayingArtworkReady();
                    break;
                case NotificationType.PlayingTracksChanged:
                    notificationHandler.HandleNowPlayingListChanged();
                    break;
                case NotificationType.FileAddedToLibrary:
                    notificationHandler.HandleFileAddedToLibrary(sourceFileUrl);
                    break;
            }
        }
    }
}
