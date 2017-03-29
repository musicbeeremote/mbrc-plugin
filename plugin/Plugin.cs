using System;
using System.IO;
using System.Reflection;
using MusicBeePlugin.ApiAdapters;
using MusicBeeRemoteCore;
using MusicBeeRemoteCore.Core;
using MusicBeeRemoteCore.Remote.Commands;
using MusicBeeRemoteCore.Remote.Events;
using MusicBeeRemoteCore.Remote.Model.Entities;

namespace MusicBeePlugin
{
    /// <summary>
    /// The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
    public partial class Plugin
    {
        private MusicBeeApiInterface _api;
        private readonly PluginInfo _about = new PluginInfo();
        private IMusicBeeRemote _musicBeeRemote;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _api = new MusicBeeApiInterface();
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
                currentVersion
            );

            var remoteBootstrap = new RemoteBootstrap();
            _musicBeeRemote = remoteBootstrap.RegisterDependencies(dependencies);

            _api.MB_AddMenuItem("mnuTools/MusicBee Remote", "Information Panel of the MusicBee Remote",
                MenuItemClicked);

            _musicBeeRemote.Start();


            _hub.Subscribe<CoverDataReadyEvent>(msg => BroadcastCover(msg.Cover));
            _hub.Subscribe<LyricsDataReadyEvent>(msg => BroadcastLyrics(msg.Lyrics));

            RequestNowPlayingTrackCover();
            RequestNowPlayingTrackLyrics();


            return _about;
        }

        private void MenuItemClicked(object sender, EventArgs args)
        {
            _musicBeeRemote.DisplayInfoWindow();
        }

        public bool Configure(IntPtr panelHandle)
        {
            _musicBeeRemote.DisplayInfoWindow();
            return true;
        }

        public void Close(PluginCloseReason reason)
        {
            /** When the plugin closes for whatever reason the SocketServer must stop **/
            _musicBeeRemote.Stop();
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
            //UserSettings.SettingsModel = SettingsController.SettingsModel;
            //UserSettings.SaveSettings("mbremote");
        }

        public void BroadcastCover(string cover)
        {
            var payload = new CoverPayload(cover, false);
            var broadcastEvent = new BroadcastEvent(Constants.NowPlayingCover);
            broadcastEvent.AddPayload(Constants.V2, cover);
            broadcastEvent.AddPayload(Constants.V3, payload);
            _hub.Publish(new BroadcastEventAvailable(broadcastEvent));
        }

        public void BroadcastLyrics(string lyrics)
        {
            var versionTwoData = !string.IsNullOrEmpty(lyrics) ? lyrics : "Lyrics Not Found";

            var lyricsPayload = new LyricsPayload(lyrics);

            var broadcastEvent = new BroadcastEvent(Constants.NowPlayingLyrics);
            broadcastEvent.AddPayload(Constants.V2, versionTwoData);
            broadcastEvent.AddPayload(Constants.V3, lyricsPayload);
            _hub.Publish(new BroadcastEventAvailable(broadcastEvent));
        }

        /// <summary>
        /// Receives event Notifications from MusicBee. It is only required if the about.ReceiveNotificationFlags = PlayerEvents.
        /// </summary>
        /// <param name="sourceFileUrl"></param>
        /// <param name="type"></param>
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            /** Perfom an action depending on the notification type **/
            switch (type)
            {
                case NotificationType.TrackChanged:
                    _musicBeeRemote.NotifyTrackChanged();

                    RequestNowPlayingTrackCover();
                    RequestTrackRating(string.Empty, string.Empty);
                    RequestLoveStatus("status", "all");
                    RequestNowPlayingTrackLyrics();
                    RequestPlayPosition("status");
                    var broadcastEvent = new BroadcastEvent(Constants.NowPlayingTrack);
                    broadcastEvent.AddPayload(Constants.V2, GetTrackInfo());
                    broadcastEvent.AddPayload(Constants.V3, GetTrackInfoV2());
                    _hub.Publish(new BroadcastEventAvailable(broadcastEvent));
                    break;
                case NotificationType.VolumeLevelChanged:
                    _musicBeeRemote.NotifyVolumeLevelChanged();

                    var volume = (int) Math.Round(_api.Player_GetVolume() * 100, 1);
                    var playerMessage = new SocketMessage(Constants.PlayerVolume, volume);
                    _hub.Publish(new PluginResponseAvailableEvent(playerMessage));
                    break;
                case NotificationType.VolumeMuteChanged:
                    _musicBeeRemote.NotifyVolumeMuteChanged();
                    var muteMessages = new SocketMessage(Constants.PlayerMute, _api.Player_GetMute());
                    _hub.Publish(new PluginResponseAvailableEvent(muteMessages));
                    break;
                case NotificationType.PlayStateChanged:
                    _musicBeeRemote.NotifyPlayStateChanged();
                    var stateMessage = new SocketMessage(Constants.PlayerState, _api.Player_GetPlayState());
                    _hub.Publish(new PluginResponseAvailableEvent(stateMessage));
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    _musicBeeRemote.NotifyLyricsReady();
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    _musicBeeRemote.NotifyArtworkReady();
                    break;
                case NotificationType.NowPlayingListChanged:
                    _musicBeeRemote.NotifyNowPlayingListChanged();
                    var playlistChangeMessages = new SocketMessage(Constants.NowPlayingListChanged, true);
                    _hub.Publish(new PluginResponseAvailableEvent(playlistChangeMessages));
                    break;
            }
        }

    }
}