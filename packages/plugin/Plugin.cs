using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using MusicBeePlugin.Host;
using MusicBeePlugin.Settings;
using FfiGen = MusicBeePlugin.Ffi.Generated;

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
        ///     The hand-wired composition root (providers + services + FFI bridge).
        /// </summary>
        private PluginHost _host;

        /// <summary>The preferences panel, built lazily in <see cref="Configure" />.</summary>
        private ConfigurationPanel _configPanel;

        /// <summary>Plugin version string, shown in the preferences panel.</summary>
        private string _version;

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
            _version = version.ToString();

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
            // Non-zero height tells MusicBee this plugin has a preferences panel;
            // MusicBee then calls Configure(panelHandle) to populate it. The panel
            // now holds only a Configure button, so it needs little room.
            _about.ConfigurationPanelHeight = 120;

            if (_api.ApiRevision < MinApiRevision)
                return _about;

            InitializeHost(version);

            // A Tools menu entry opens the same settings dialog as the Configure
            // button, matching the classic plugin's layout.
            _api.MB_AddMenuItem(
                "mnuTools/MusicBee Remote",
                "MusicBee Remote: open settings",
                (sender, args) => OpenSettingsDialog());

            return _about;
        }

        /// <summary>
        ///     Open the settings dialog (shared by the Configure button and the
        ///     Tools menu entry). No-op if the host failed to start.
        /// </summary>
        private void OpenSettingsDialog()
        {
            if (_host == null)
                return;

            using (var dialog = new SettingsDialog(_host, _version))
                dialog.ShowDialog();
        }

        /// <summary>
        ///     Builds the composition root and boots the Rust core. Wrapped in a
        ///     catch-all so a startup failure leaves the plugin degraded (remote
        ///     off) rather than crashing MusicBee.
        /// </summary>
        /// <param name="version">The plugin version from assembly.</param>
        private void InitializeHost(Version version)
        {
            try
            {
                _host = new PluginHost(_api, _api.Setting_GetPersistentStoragePath(), version);
                _host.Start();
            }
            catch (Exception ex)
            {
                // Log the error to a fallback location and ensure the plugin
                // never crashes MusicBee.
                try
                {
                    var fallbackPath = Path.Combine(
                        _api.Setting_GetPersistentStoragePath(),
                        "mb_remote",
                        "initialization_error.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath));
                    File.AppendAllText(fallbackPath,
                        $"[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.ffffffZ}] FATAL: Plugin initialization failed: {ex}\n");
                }
                catch
                {
                    // If logging fails, continue silently to prevent MusicBee crash
                }

                // Ensure _host is null so other methods handle it gracefully.
                _host = null;
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
            // The core owns settings; the panel reads/writes them over the FFI.
            if (_host == null || panelHandle == IntPtr.Zero)
                return false;

            var panel = Control.FromHandle(panelHandle);
            if (panel == null)
                return false;

            _configPanel = new ConfigurationPanel(_host, _version);
            _configPanel.AttachTo(panel);
            return true;
        }

        /// <summary>
        ///     Called when MusicBee closes or the plugin is disabled.
        /// </summary>
        /// <param name="reason">The reason for closing.</param>
        public void Close(PluginCloseReason reason)
        {
            _host?.Dispose();
            _host = null;
        }

        /// <summary>
        ///     Cleans up any persisted files during the plugin uninstall.
        /// </summary>
        public void Uninstall()
        {
            try
            {
                var settingsFolder = Path.Combine(
                    _api.Setting_GetPersistentStoragePath(),
                    "mb_remote");
                if (Directory.Exists(settingsFolder))
                    Directory.Delete(settingsFolder, true);
            }
            catch
            {
                // Best-effort cleanup; never throw out of Uninstall.
            }
        }

        /// <summary>
        ///     Called by MusicBee when the user clicks Save/Apply in the Preferences
        ///     screen. The embedded panel holds only a Configure button now; settings
        ///     are edited and persisted in the settings dialog, so there is nothing to
        ///     apply here.
        /// </summary>
        public void SaveSettings()
        {
        }

        /// <summary>
        ///     Receives event Notifications from MusicBee. It is only required if the about.ReceiveNotificationFlags =
        ///     PlayerEvents.
        /// </summary>
        /// <param name="sourceFileUrl"></param>
        /// <param name="type"></param>
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (_host == null)
                return;

            // Map MusicBee's notification to the core's NotificationType (the
            // generated FFI enum). Only the events the core re-queries are
            // forwarded; anything else is ignored.
            FfiGen.NotificationType coreType;
            switch (type)
            {
                case NotificationType.TrackChanged:
                    coreType = FfiGen.NotificationType.TrackChanged;
                    break;
                case NotificationType.PlayStateChanged:
                    coreType = FfiGen.NotificationType.PlayStateChanged;
                    break;
                case NotificationType.VolumeLevelChanged:
                    coreType = FfiGen.NotificationType.VolumeLevelChanged;
                    break;
                case NotificationType.VolumeMuteChanged:
                    coreType = FfiGen.NotificationType.VolumeMuteChanged;
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    coreType = FfiGen.NotificationType.NowPlayingLyricsReady;
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    coreType = FfiGen.NotificationType.NowPlayingArtworkReady;
                    break;
                case NotificationType.PlayingTracksChanged:
                    coreType = FfiGen.NotificationType.NowPlayingListChanged;
                    break;
                case NotificationType.FileAddedToLibrary:
                    coreType = FfiGen.NotificationType.FileAddedToLibrary;
                    break;
                case NotificationType.LibrarySwitched:
                    coreType = FfiGen.NotificationType.LibrarySwitched;
                    break;
                default:
                    return;
            }

            _host.HandleNotification((int)coreType);
        }
    }
}
