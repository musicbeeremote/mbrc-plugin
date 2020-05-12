using System;
using System.IO;
using System.Reflection;
using MusicBeePlugin.ApiAdapters;
using MusicBeeRemote.Core;

namespace MusicBeePlugin
{
    /// <summary>
    /// The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
    public partial class Plugin
    {
        private readonly PluginInfo _about = new PluginInfo();
        private MusicBeeApiInterface _api;
        private IMusicBeeRemotePlugin _musicBeeRemotePlugin;
        private RemoteBootstrap _bootstrap;

        /// <summary>
        /// Initializes the MusicBee Remote plugin.
        /// </summary>
        /// <param name="apiInterfacePtr">The API interface of MusicBee.</param>
        /// <returns>The plugin info.</returns>
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _api = default;
            _api.Initialise(apiInterfacePtr);

            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "MusicBee Remote: Plugin";
            _about.Description = "Remote Control for server to be used with android application.";
            _about.Author = "Konstantinos Paparas (aka Kelsos)";
            _about.TargetApplication = "MusicBee Remote";

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var currentVersion = version.ToString();

            // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            _about.Type = PluginType.General;
            _about.VersionMajor = Convert.ToInt16(version.Major);
            _about.VersionMinor = Convert.ToInt16(version.Minor);
            _about.Revision = Convert.ToInt16(version.Build);
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;

            if (_api.ApiRevision < MinApiRevision)
            {
                return _about;
            }

            // Initialize the required adapters for the plugin to operate.
            var libraryApiAdapter = new LibraryApiAdapter(_api);
            var nowPlayingApiAdapter = new NowPlayingApiAdapter(_api);
            var outputApiAdapter = new OutputApiAdapter(_api);
            var playerApiAdapter = new PlayerApiAdapter(_api);
            var queueAdapter = new QueueAdapter(_api);
            var trackApiAdapter = new TrackApiAdapter(_api);
            var invokeHandler = new InvokeHandler(_api);
            var baseStoragePath = _api.Setting_GetPersistentStoragePath();

            var dependencies = new MusicBeeDependencies(
                libraryApiAdapter,
                nowPlayingApiAdapter,
                outputApiAdapter,
                playerApiAdapter,
                queueAdapter,
                trackApiAdapter,
                invokeHandler,
                baseStoragePath,
                currentVersion);

            _bootstrap = new RemoteBootstrap();
            _musicBeeRemotePlugin = _bootstrap.BootStrap(dependencies);

            const string menuItemDescription = "Information Panel of the MusicBee Remote";
            _api.MB_AddMenuItem("mnuTools/MusicBee Remote: Settings", menuItemDescription, MenuItemClicked);

#if DEBUG
            // Experimental feature only supported on debug
            AddPartyMode();
#endif

            _musicBeeRemotePlugin.Start();

            return _about;
        }

        /// <summary>
        /// Called by MusicBee when the user presses configure in the plugin settings.
        /// </summary>
        /// <param name="panelHandle">A pointer to a panel handle.</param>
        /// <returns>Always returns true.</returns>
        public bool Configure(IntPtr panelHandle)
        {
            _musicBeeRemotePlugin.DisplayInfoWindow();
            return true;
        }

        /// <summary>
        /// Called by MusicBee when it closes the Remote plugin for any reason.
        /// </summary>
        /// <param name="reason">The reason for the plugin close.</param>
        public void Close(PluginCloseReason reason)
        {
            // Terminated the plugin core, when the plugin received the close event
            _musicBeeRemotePlugin.Terminate();
            _bootstrap.Dispose();
        }

        /// <summary>
        /// Cleans up any persisted files during the plugin uninstall.
        /// </summary>
        public void Uninstall()
        {
            var settingsFolder = _api.Setting_GetPersistentStoragePath + "\\mb_remote";
            if (Directory.Exists(settingsFolder))
            {
                Directory.Delete(settingsFolder);
            }
        }

        /// <summary>
        /// Called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        /// Used to save the temporary Plugin SettingsModel if the have changed.
        /// </summary>
        public void SaveSettings()
        {
            // This method is not used, the plugin has it's own save button
        }

        /// <summary>
        /// Receives event Notifications from MusicBee. It is only required if the about.ReceiveNotificationFlags = PlayerEvents.
        /// </summary>
        /// <param name="sourceFileUrl">The path of the file responsible for generating then notification.</param>
        /// <param name="type">The type of the notification.</param>
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            switch (type)
            {
                case NotificationType.TrackChanged:
                    _musicBeeRemotePlugin.NotifyTrackChanged();
                    break;
                case NotificationType.VolumeLevelChanged:
                    _musicBeeRemotePlugin.NotifyVolumeLevelChanged();
                    break;
                case NotificationType.VolumeMuteChanged:
                    _musicBeeRemotePlugin.NotifyVolumeMuteChanged();
                    break;
                case NotificationType.PlayStateChanged:
                    _musicBeeRemotePlugin.NotifyPlayStateChanged();
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    _musicBeeRemotePlugin.NotifyLyricsReady();
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    _musicBeeRemotePlugin.NotifyArtworkReady();
                    break;
                case NotificationType.PlayingTracksChanged:
                    _musicBeeRemotePlugin.NotifyNowPlayingListChanged();
                    break;
            }
        }

        private void AddPartyMode()
        {
            const string description = "Control panel of the party mode functionality";
            const string key = "mnuTools/MusicBee Remote: Party Mode";
            _api.MB_AddMenuItem(key, description, (sender, args) =>
            {
                _musicBeeRemotePlugin.DisplayPartyModeWindow();
            });
        }

        private void MenuItemClicked(object sender, EventArgs args)
        {
            _musicBeeRemotePlugin.DisplayInfoWindow();
        }
    }
}
