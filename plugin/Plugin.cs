using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using System.Windows.Forms;
using System.Xml.Linq;
using MusicBeePlugin.AndroidRemote;
using MusicBeePlugin.AndroidRemote.Commands;
using MusicBeePlugin.AndroidRemote.Controller;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.AndroidRemote.Utilities;
using NLog;
using NLog.Config;
using NLog.Targets;
using ServiceStack;
using ServiceStack.Text;
using Timer = System.Timers.Timer;

namespace MusicBeePlugin
{
    /// <summary>
    /// The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
    public partial class Plugin : InfoWindow.IOnDebugSelectionChanged
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The mb api interface.
        /// </summary>
        private MusicBeeApiInterface _api;

        /// <summary>
        /// The _about.
        /// </summary>
        private readonly PluginInfo _about = new PluginInfo();

        /// <summary>
        /// The timer.
        /// </summary>
        private Timer _timer;

        private Timer _positionUpdateTimer;

        /// <summary>
        /// The shuffle.
        /// </summary>
        private ShuffleState _shuffleState;

        /// <summary>
        /// Represents the current repeat mode.
        /// </summary>
        private RepeatMode _repeat;

        /// <summary>
        /// The scrobble.
        /// </summary>
        private bool _scrobble;

        private bool BuildingCoverCache { get; set; }

        /// <summary>
        /// Returns the plugin instance (Singleton);
        /// </summary>
        public static Plugin
            Instance { get; private set; }

        private InfoWindow _mWindow;
        private bool _userChangingShuffle;

        private void InitializeCoverCache()
        {
            BuildingCoverCache = true;
            BroadcastCoverCacheBuildStatus();
            PrepareCache();
            BuildCoverCache();
            BuildingCoverCache = false;
            BroadcastCoverCacheBuildStatus();
        }

        public void BroadcastCoverCacheBuildStatus(string clientId = null)
        {
            var message = new SocketMessage(Constants.LibraryCoverCacheBuildStatus, BuildingCoverCache);
            SendReply(message.ToJsonString(), clientId);
        }

        /// <summary>
        /// This function initialized the Plugin.
        /// </summary>
        /// <param name="apiInterfacePtr"></param>
        /// <returns></returns>
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            Instance = this;
            JsConfig.ExcludeTypeInfo = true;
            Configuration.Register(Controller.Instance);

            _api = new MusicBeeApiInterface();
            _api.Initialise(apiInterfacePtr);

            UserSettings.Instance.SetStoragePath(_api.Setting_GetPersistentStoragePath());
            UserSettings.Instance.LoadSettings();

            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "MusicBee Remote: Plugin";
            _about.Description = "Remote Control for server to be used with android application.";
            _about.Author = "Konstantinos Paparas (aka Kelsos)";
            _about.TargetApplication = "MusicBee Remote";

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            UserSettings.Instance.CurrentVersion = version.ToString();

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

#if DEBUG
            InitializeLoggingConfiguration(UserSettings.Instance.FullLogPath, LogLevel.Debug);
#else
            var logLevel = UserSettings.Instance.DebugLogEnabled ? LogLevel.Debug : LogLevel.Error;
            InitializeLoggingConfiguration(UserSettings.Instance.FullLogPath, logLevel);
#endif

            StartPlayerStatusMonitoring();

            _api.MB_AddMenuItem("mnuTools/MusicBee Remote", "Information Panel of the MusicBee Remote",
                MenuItemClicked);

            EventBus.FireEvent(new MessageEvent(EventType.ActionSocketStart));
            EventBus.FireEvent(new MessageEvent(EventType.InitializeModel));
            EventBus.FireEvent(new MessageEvent(EventType.StartServiceBroadcast));
            EventBus.FireEvent(new MessageEvent(EventType.ShowFirstRunDialog));

            _positionUpdateTimer = new Timer(20000);
            _positionUpdateTimer.Elapsed += PositionUpdateTimerOnElapsed;
            _positionUpdateTimer.Enabled = true;

            Utilities.StoragePath = UserSettings.Instance.StoragePath;

            if (!BuildingCoverCache)
            {
                _api.MB_CreateBackgroundTask(InitializeCoverCache, Form.ActiveForm);    
            }

            return _about;
        }

        private void PositionUpdateTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (_api.Player_GetPlayState() == PlayState.Playing)
            {
                RequestPlayPosition("status");
            }
        }

        /// <summary>
        /// Menu Item click handler. It handles the Tools -> MusicBee Remote entry click and opens the respective info panel.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="args">
        /// The args.
        /// </param>
        private void MenuItemClicked(object sender, EventArgs args)
        {
            DisplayInfoWindow();
        }

        public void UpdateWindowStatus(bool status)
        {
            if (_mWindow != null && _mWindow.Visible)
            {
                _mWindow.UpdateSocketStatus(status);
            }
        }

        /// <summary>
        /// The function populates the local player status variables and then
        /// starts the Monitoring of the player status every 1000 ms to retrieve
        /// any changes.
        /// </summary>
        private void StartPlayerStatusMonitoring()
        {
            _scrobble = _api.Player_GetScrobbleEnabled();
            _repeat = _api.Player_GetRepeat();
            _shuffleState = GetShuffleState();
            _timer = new Timer {Interval = 1000};
            _timer.Elapsed += HandleTimerElapsed;
            _timer.Enabled = true;
        }

        /// <summary>
        /// This function runs periodically every 1000 ms as the timer ticks and
        /// checks for changes on the player status.  If a change is detected on
        /// one of the monitored variables the function will fire an event with
        /// the new status.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="args">
        /// The event arguments.
        /// </param>
        private void HandleTimerElapsed(object sender, ElapsedEventArgs args)
        {
            if (GetShuffleState() != _shuffleState && !_userChangingShuffle)
            {
                _shuffleState = GetShuffleState();
                EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, new SocketMessage(
                        Constants.PlayerShuffle, _shuffleState)
                    .ToJsonString()));
            }

            if (_api.Player_GetScrobbleEnabled() != _scrobble)
            {
                _scrobble = _api.Player_GetScrobbleEnabled();
                EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerScrobble, _scrobble)
                        .ToJsonString()));
            }

            if (_api.Player_GetRepeat() != _repeat)
            {
                _repeat = _api.Player_GetRepeat();
                var payload = new SocketMessage(Constants.PlayerRepeat, _repeat).ToJsonString();
                SendReply(payload);
            }
        }

        private static void SendReply(string payload, string clientId = "")
        {
            MessageEvent message;
            if (string.IsNullOrEmpty(clientId))
            {
                message = new MessageEvent(
                    EventType.ReplyAvailable,
                    payload);
            }
            else
            {
                message = new MessageEvent(
                    EventType.ReplyAvailable,
                    payload, clientId);
            }

            EventBus.FireEvent(message);
        }

        public void OpenInfoWindow()
        {
            var hwnd = _api.MB_GetWindowHandle();
            var mb = (Form) Control.FromHandle(hwnd);
            mb.Invoke(new MethodInvoker(DisplayInfoWindow));
        }

        private void DisplayInfoWindow()
        {
            if (_mWindow == null || !_mWindow.Visible)
            {
                _mWindow = new InfoWindow();
                _mWindow.SetOnDebugSelectionListener(this);
            }

            _mWindow.Show();
        }

        /// <summary>
        /// Creates the MusicBee plugin Configuration panel.
        /// </summary>
        /// <param name="panelHandle">
        /// The panel handle.
        /// </param>
        /// <returns>
        /// The <see cref="bool"/>.
        /// </returns>
        public bool Configure(IntPtr panelHandle)
        {
            DisplayInfoWindow();
            return true;
        }

        /// <summary>
        /// The close.
        /// </summary>
        /// <param name="reason">
        /// The reason.
        /// </param>
        public void Close(PluginCloseReason reason)
        {
            /** When the plugin closes for whatever reason the SocketServer must stop **/
            EventBus.FireEvent(new MessageEvent(EventType.ActionSocketStop));
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

        public static void BroadcastCover(string cover)
        {
            var payload = new CoverPayload(cover, false);
            var broadcastEvent = new BroadcastEvent(Constants.NowPlayingCover);
            broadcastEvent.addPayload(Constants.V2, cover);
            broadcastEvent.addPayload(Constants.V3, payload);
            EventBus.FireEvent(new MessageEvent(EventType.BroadcastEvent, broadcastEvent));
        }

        public static void BroadcastLyrics(string lyrics)
        {
            var versionTwoData = !string.IsNullOrEmpty(lyrics) ? lyrics : "Lyrics Not Found";

            var lyricsPayload = new LyricsPayload(lyrics);

            var broadcastEvent = new BroadcastEvent(Constants.NowPlayingLyrics);
            broadcastEvent.addPayload(Constants.V2, versionTwoData);
            broadcastEvent.addPayload(Constants.V3, lyricsPayload);
            EventBus.FireEvent(new MessageEvent(EventType.BroadcastEvent, broadcastEvent));
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
                    RequestNowPlayingTrackCover();
                    RequestTrackRating("-1", string.Empty);
                    RequestLoveStatus("status", "all");
                    RequestNowPlayingTrackLyrics();
                    RequestPlayPosition("status");
                    var broadcastEvent = new BroadcastEvent(Constants.NowPlayingTrack);
                    broadcastEvent.addPayload(Constants.V2, GetTrackInfo());
                    broadcastEvent.addPayload(Constants.V3, GetTrackInfoV2());
                    EventBus.FireEvent(new MessageEvent(EventType.BroadcastEvent, broadcastEvent));
                    break;
                case NotificationType.VolumeLevelChanged:
                    var payload = new SocketMessage(Constants.PlayerVolume,
                        (int)
                        Math.Round(
                            _api.Player_GetVolume() * 100,
                            1)).ToJsonString();
                    SendReply(payload);
                    break;
                case NotificationType.VolumeMuteChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.PlayerMute,
                            _api.Player_GetMute()).ToJsonString()
                    ));
                    break;
                case NotificationType.PlayStateChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.PlayerState,
                            _api
                                .Player_GetPlayState
                                    ()).ToJsonString
                            ()));
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    if (_api.ApiRevision >= 17)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.NowPlayingLyricsChange,
                            _api.NowPlaying_GetDownloadedLyrics()));
                    }

                    break;
                case NotificationType.NowPlayingArtworkReady:
                    if (_api.ApiRevision >= 17)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange,
                            _api.NowPlaying_GetDownloadedArtwork()));
                    }

                    break;
                case NotificationType.NowPlayingListChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingListChanged, true).ToJsonString()));
                    break;
                case NotificationType.FileAddedToLibrary:
                    var hash = CacheCover(sourceFileUrl);
                    var artist = GetAlbumArtistForTrack(sourceFileUrl);
                    var album = GetAlbumForTrack(sourceFileUrl);
                    var key = Utilities.CoverIdentifier(artist, album);
                    CoverCache.Instance.Update(key, hash);
                    break;
            }
        }

        private NowPlayingTrack GetTrackInfo()
        {
            var nowPlayingTrack = new NowPlayingTrack
            {
                Artist = GetNowPlayingArtist(),
                Album = GetNowPlayingAlbum(),
                Year = GetNowPlayingYear()
            };
            nowPlayingTrack.SetTitle(GetNowPlayingTrackTitle(), GetNowPlayingFileUrl());
            return nowPlayingTrack;
        }


        private NowPlayingTrackV2 GetTrackInfoV2()
        {
            var fileUrl = GetNowPlayingFileUrl();
            var nowPlayingTrack = new NowPlayingTrackV2
            {
                Artist = GetNowPlayingArtist(),
                Album = GetNowPlayingAlbum(),
                Year = GetNowPlayingYear(),
                Path = fileUrl
            };
            nowPlayingTrack.SetTitle(GetNowPlayingTrackTitle(), fileUrl);
            return nowPlayingTrack;
        }

        public NowPlayingDetails GetPlayingTrackDetails()
        {
            var nowPlayingTrack = new NowPlayingDetails
            {
                AlbumArtist = _api.NowPlaying_GetFileTag(MetaDataType.AlbumArtist).Cleanup(),
                Genre = _api.NowPlaying_GetFileTag(MetaDataType.Genre).Cleanup(),
                TrackNo = _api.NowPlaying_GetFileTag(MetaDataType.TrackNo).Cleanup(),
                TrackCount = _api.NowPlaying_GetFileTag(MetaDataType.TrackCount).Cleanup(),
                DiscNo = _api.NowPlaying_GetFileTag(MetaDataType.DiscNo).Cleanup(),
                DiscCount = _api.NowPlaying_GetFileTag(MetaDataType.DiscCount).Cleanup(),
                Grouping = _api.NowPlaying_GetFileTag(MetaDataType.Grouping).Cleanup(),
                Publisher = _api.NowPlaying_GetFileTag(MetaDataType.Publisher).Cleanup(),
                RatingAlbum = _api.NowPlaying_GetFileTag(MetaDataType.RatingAlbum).Cleanup(),
                Composer = _api.NowPlaying_GetFileTag(MetaDataType.Composer).Cleanup(),
                Comment = _api.NowPlaying_GetFileTag(MetaDataType.Comment).Cleanup(),
                Encoder = _api.NowPlaying_GetFileTag(MetaDataType.Encoder).Cleanup(),

                Kind = _api.NowPlaying_GetFileProperty(FilePropertyType.Kind).Cleanup(),
                Format = _api.NowPlaying_GetFileProperty(FilePropertyType.Format).Cleanup(),
                Size = _api.NowPlaying_GetFileProperty(FilePropertyType.Size).Cleanup(),
                Channels = _api.NowPlaying_GetFileProperty(FilePropertyType.Channels).Cleanup(),
                SampleRate = _api.NowPlaying_GetFileProperty(FilePropertyType.SampleRate).Cleanup(),
                Bitrate = _api.NowPlaying_GetFileProperty(FilePropertyType.Bitrate).Cleanup(),
                DateModified = _api.NowPlaying_GetFileProperty(FilePropertyType.DateModified).Cleanup(),
                DateAdded = _api.NowPlaying_GetFileProperty(FilePropertyType.DateAdded).Cleanup(),
                LastPlayed = _api.NowPlaying_GetFileProperty(FilePropertyType.LastPlayed).Cleanup(),
                PlayCount = _api.NowPlaying_GetFileProperty(FilePropertyType.PlayCount).Cleanup(),
                SkipCount = _api.NowPlaying_GetFileProperty(FilePropertyType.SkipCount).Cleanup(),
                Duration = _api.NowPlaying_GetFileProperty(FilePropertyType.Duration).Cleanup()
            };

            return nowPlayingTrack;
        }

        private string GetNowPlayingFileUrl()
        {
            return _api.NowPlaying_GetFileUrl();
        }

        private string GetNowPlayingArtist()
        {
            return _api.NowPlaying_GetFileTag(MetaDataType.Artist);
        }

        private string GetNowPlayingTrackTitle()
        {
            return _api.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
        }

        private string GetNowPlayingYear()
        {
            return _api.NowPlaying_GetFileTag(MetaDataType.Year);
        }

        private string GetNowPlayingAlbum()
        {
            return _api.NowPlaying_GetFileTag(MetaDataType.Album);
        }

        /// <summary>
        /// When called plays the next track.
        /// </summary>
        /// <returns></returns>
        public void RequestNextTrack(string clientId)
        {
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerNext,
                        _api.Player_PlayNextTrack()).ToJsonString()));
        }

        /// <summary>
        /// When called stops the playback.
        /// </summary>
        /// <returns></returns>
        public void RequestStopPlayback(string clientId)
        {
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerStop,
                        _api.Player_Stop()).ToJsonString()));
        }

        /// <summary>
        /// When called changes the play/pause state or starts playing a track if the status is stopped.
        /// </summary>
        /// <returns></returns>
        public void RequestPlayPauseTrack(string clientId)
        {
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerPlayPause,
                        _api.Player_PlayPause()).ToJsonString()));
        }

        /// <summary>
        /// When called plays the previous track.
        /// </summary>
        /// <returns></returns>
        public void RequestPreviousTrack(string clientId)
        {
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerPrevious,
                        _api.Player_PlayPreviousTrack()).ToJsonString()));
        }

        /// <summary>
        /// When called if the volume string is an integer in the range [0,100] it
        /// changes the volume to the specific value and returns the new value.
        /// In any other case it just returns the current value for the volume.
        /// </summary>
        /// <param name="volume"> </param>
        public void RequestVolumeChange(int volume)
        {
            if (volume >= 0)
            {
                _api.Player_SetVolume((float) volume / 100);
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerVolume,
                        ((int) Math.Round(_api.Player_GetVolume() * 100, 1))).ToJsonString()));

            if (_api.Player_GetMute())
            {
                _api.Player_SetMute(false);
            }
        }

        /// <summary>
        /// Changes the player shuffle state. If the StateAction is Toggle then the current state is switched with it's opposite,
        /// if it is State the current state is dispatched with an Event.
        /// </summary>
        /// <param name="action"></param>
        public void RequestShuffleState(StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                _api.Player_SetShuffle(!_api.Player_GetShuffle());
            }

            EventBus.FireEvent(
                new MessageEvent(
                    EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerShuffle,
                        _api.Player_GetShuffle()).ToJsonString()));
        }

        /// <summary>
        /// Changes the player shuffle and autodj state following the model of MusicBee.
        /// </summary>
        /// <param name="action"></param>
        public void RequestAutoDjShuffleState(StateAction action)
        {
            var shuffleEnabled = _api.Player_GetShuffle();
            var autoDjEnabled = _api.Player_GetAutoDjEnabled();

            if (action != StateAction.Toggle) return;
            if (shuffleEnabled && !autoDjEnabled)
            {
                var success = _api.Player_StartAutoDj();
                if (success)
                {
                    _shuffleState = ShuffleState.autodj;
                }
            }
            else if (autoDjEnabled)
            {
                _api.Player_EndAutoDj();
            }
            else
            {
                var success = _api.Player_SetShuffle(true);
                if (success)
                {
                    _shuffleState = ShuffleState.shuffle;
                }
            }

            var socketMessage = new SocketMessage(Constants.PlayerShuffle, _shuffleState);
            EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, socketMessage.SerializeToString()));
        }

        private ShuffleState GetShuffleState()
        {
            var shuffleEnabled = _api.Player_GetShuffle();
            var autoDjEnabled = _api.Player_GetAutoDjEnabled();
            var state = ShuffleState.off;
            if (shuffleEnabled && !autoDjEnabled)
            {
                state = ShuffleState.shuffle;
            }
            else if (autoDjEnabled)
            {
                state = ShuffleState.autodj;
            }

            return state;
        }

        /// <summary>
        /// Changes the player mute state. If the StateAction is Toggle then the current state is switched with it's opposite,
        /// if it is State the current state is dispatched with an Event.
        /// </summary>
        /// <param name="action"></param>
        public void RequestMuteState(StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                _api.Player_SetMute(!_api.Player_GetMute());
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerMute,
                        _api.Player_GetMute()).ToJsonString()));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="action"></param>
        public void RequestScrobblerState(StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                _api.Player_SetScrobbleEnabled(!_api.Player_GetScrobbleEnabled());
            }

            EventBus.FireEvent(
                new MessageEvent(
                    EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerScrobble,
                        _api.Player_GetScrobbleEnabled()).ToJsonString()));
        }

        /// <summary>
        /// If the action equals toggle then it changes the repeat state, in any other case
        /// it just returns the current value of the repeat.
        /// </summary>
        /// <param name="action">toggle or state</param>
        /// <returns>Repeat state: None, All, One</returns>
        public void RequestRepeatState(StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                switch (_api.Player_GetRepeat())
                {
                    case RepeatMode.None:
                        _api.Player_SetRepeat(RepeatMode.All);
                        break;
                    case RepeatMode.All:
                        _api.Player_SetRepeat(RepeatMode.One);
                        break;
                    case RepeatMode.One:
                        _api.Player_SetRepeat(RepeatMode.None);
                        break;
                }
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerRepeat,
                        _api.Player_GetRepeat()).ToJsonString()));
        }

        public void RequestNowPlayingListOrdered(string clientId, int offset = 0, int limit = 100)
        {
            _api.NowPlayingList_QueryFiles(null);

            var tracks = new List<NowPlaying>();
            var position = 1;
            var itemIndex = _api.NowPlayingList_GetCurrentIndex();
            while (position <= limit)
            {
                var trackPath = _api.NowPlayingList_GetListFileUrl(itemIndex);

                if (string.IsNullOrEmpty(trackPath))
                    break;

                var artist = _api.Library_GetFileTag(trackPath, MetaDataType.Artist);
                var album = _api.Library_GetFileTag(trackPath, MetaDataType.Album);
                var albumArtist = _api.Library_GetFileTag(trackPath, MetaDataType.AlbumArtist);
                var title = _api.Library_GetFileTag(trackPath, MetaDataType.TrackTitle);

                if (string.IsNullOrEmpty(title))
                {
                    var index = trackPath.LastIndexOf('\\');
                    title = trackPath.Substring(index + 1);
                }

                NowPlaying track;
                if (Utilities.IsAndroid(clientId))
                {
                    track = new NowPlaying
                    {
                        Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                        Album = album,
                        AlbumArtist = albumArtist,
                        Title = title,
                        Position = itemIndex,
                        Path = trackPath
                    };
                }
                else
                {
                    track = new NowPlaying
                    {
                        Artist = artist,
                        Album = album,
                        AlbumArtist = albumArtist,
                        Title = title,
                        Position = itemIndex,
                        Path = trackPath
                    };
                }

                tracks.Add(track);
                itemIndex = _api.NowPlayingList_GetNextIndex(position);
                position++;
            }

            var total = tracks.Count;
            var message = new SocketMessage
            {
                Context = Constants.NowPlayingList,
                Data = new Page<NowPlaying>
                {
                    Data = tracks,
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };
            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        public void RequestNowPlayingListPage(string clientId, int offset = 0, int limit = 4000)
        {
            _api.NowPlayingList_QueryFiles(null);

            var tracks = new List<NowPlaying>();
            var position = 1;
            while (true)
            {
                var trackPath = _api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(trackPath))
                    break;

                var artist = _api.Library_GetFileTag(trackPath, MetaDataType.Artist);
                var title = _api.Library_GetFileTag(trackPath, MetaDataType.TrackTitle);

                if (string.IsNullOrEmpty(title))
                {
                    var index = trackPath.LastIndexOf('\\');
                    title = trackPath.Substring(index + 1);
                }

                var track = new NowPlaying
                {
                    Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                    Title = title,
                    Position = position,
                    Path = trackPath
                };

                tracks.Add(track);
                position++;
            }

            var total = tracks.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.NowPlayingList,
                Data = new Page<NowPlaying>
                {
                    Data = offset > total ? new List<NowPlaying>() : tracks.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };
            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        public void RequestNowPlayingList(string clientId)
        {
            _api.NowPlayingList_QueryFiles(null);

            var trackList = new List<NowPlayingListTrack>();
            var position = 1;
            while (position <= 5000)
            {
                var trackPath = _api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(trackPath))
                    break;

                var artist = _api.Library_GetFileTag(trackPath, MetaDataType.Artist);
                var title = _api.Library_GetFileTag(trackPath, MetaDataType.TrackTitle);

                if (string.IsNullOrEmpty(title))
                {
                    var index = trackPath.LastIndexOf('\\');
                    title = trackPath.Substring(index + 1);
                }

                var track = new NowPlayingListTrack
                {
                    Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                    Title = title,
                    Position = position,
                    Path = trackPath
                };
                trackList.Add(track);
                position++;
            }

            var jsonString = new SocketMessage(Constants.NowPlayingList, trackList).ToJsonString();
            var messageEvent = new MessageEvent(EventType.ReplyAvailable, jsonString, clientId);
            EventBus.FireEvent(messageEvent);
        }

        public void RequestOutputDevice(string clientId)
        {
            string[] deviceNames;
            string activeDeviceName;

            _api.Player_GetOutputDevices(out deviceNames, out activeDeviceName);

            var currentDevices = new OutputDevice(deviceNames, activeDeviceName);

            SendReply(new SocketMessage(Constants.PlayerOutput,
                currentDevices).ToJsonString(), clientId);
        }

        /// <summary>
        /// If the given rating string is not null or empty and the value of the string is a float number in the [0,5]
        /// the function will set the new rating as the current track's new track rating. In any other case it will
        /// just return the rating for the current track.
        /// </summary>
        /// <param name="rating">New Track Rating</param>
        /// <param name="clientId"> </param>
        /// <returns>Track Rating</returns>
        public void RequestTrackRating(string rating, string clientId)
        {
            var currentTrack = _api.NowPlaying_GetFileUrl();
            var decimalSeparator = Convert.ToChar(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

            try
            {
                rating = rating.Replace('.', decimalSeparator);
                float fRating;
                if (!float.TryParse(rating, out fRating))
                {
                    fRating = -1;
                }

                if (fRating >= 0 && fRating <= 5 || rating == "")
                {
                    var value = rating == ""
                        ? rating.ToString(CultureInfo.CurrentCulture)
                        : fRating.ToString(CultureInfo.CurrentCulture);
                    _api.Library_SetFileTag(currentTrack, MetaDataType.Rating, value);
                    _api.Library_CommitTagsToFile(currentTrack);
                    _api.Player_GetShowRatingTrack();
                    _api.MB_RefreshPanels();
                }

                rating = _api.Library_GetFileTag(currentTrack, MetaDataType.Rating).Replace(decimalSeparator, '.');

                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingRating,
                            rating).ToJsonString()));
            }
            catch (Exception e)
            {
                _logger.Error(e, "On Rating call");
            }
        }

        /// <summary>
        /// Requests the Now Playing track lyrics. If the lyrics are available then they are dispatched along with
        /// and event. If not, and the ApiRevision is equal or greater than r17 a request for the downloaded lyrics
        /// is initiated. The lyrics are dispatched along with and event when ready.
        /// </summary>
        public void RequestNowPlayingTrackLyrics()
        {
            var lyrics = _api.NowPlaying_GetLyrics();

            if (!string.IsNullOrEmpty(lyrics))
            {
                //no additional actions
            }
            else if (_api.ApiRevision >= 17)
            {
                lyrics = _api.NowPlaying_GetDownloadedLyrics();
            }
            else
            {
                lyrics = string.Empty;
            }

            //update LyricsCoverModel cache
            EventBus.FireEvent(new MessageEvent(EventType.NowPlayingLyricsChange, lyrics));

            BroadcastLyrics(lyrics);
        }

        /// <summary>
        /// Requests the Now Playing Track Cover. If the cover is available it is dispatched along with an event.
        /// If not, and the ApiRevision is equal or greater than r17 a request for the downloaded artwork is
        /// initiated. The cover is dispatched along with an event when ready.
        /// </summary>
        public void RequestNowPlayingTrackCover()
        {
            if (!string.IsNullOrEmpty(_api.NowPlaying_GetArtwork()))
            {
                EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange,
                    _api.NowPlaying_GetArtwork()));
            }
            else if (_api.ApiRevision >= 17)
            {
                var cover = _api.NowPlaying_GetDownloadedArtwork();
                EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange,
                    !string.IsNullOrEmpty(cover) ? cover : string.Empty));
            }
            else
            {
                EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange, String.Empty));
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="request"></param>
        public void RequestPlayPosition(string request)
        {
            if (!request.Contains("status"))
            {
                int newPosition;
                if (int.TryParse(request, out newPosition))
                {
                    _api.Player_SetPosition(newPosition);
                }
            }

            var currentPosition = _api.Player_GetPosition();
            var totalDuration = _api.NowPlaying_GetDuration();

            var position = new
            {
                current = currentPosition,
                total = totalDuration
            };

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingPosition, position).ToJsonString()));
        }

        /// <summary>
        /// Searches in the Now playing list for the track specified and plays it.
        /// </summary>
        /// <param name="index">The track to play</param>
        /// <param name="isAndroid"></param>
        /// <returns></returns>
        public void NowPlayingPlay(string index, bool isAndroid)
        {
            var result = false;
            if (int.TryParse(index, out var trackIndex))
            {
                _api.NowPlayingList_QueryFiles(null);
                var trackToPlay = _api.NowPlayingList_GetListFileUrl(isAndroid ? trackIndex - 1 : trackIndex);
                if (!string.IsNullOrEmpty(trackToPlay))
                    result = _api.NowPlayingList_PlayNow(trackToPlay);
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListPlay,
                        result).ToJsonString()));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="index"></param>
        /// <param name="clientId"></param>
        public void NowPlayingListRemoveTrack(int index, string clientId)
        {
            var reply = new
            {
                success = _api.NowPlayingList_RemoveAt(index),
                index
            };
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListRemove,
                        reply).ToJsonString(), clientId));
        }

        /// <summary>
        /// This function requests or changes the AutoDJ functionality's state.
        /// </summary>
        /// <param name="action">
        /// The action can be either toggle or state.
        /// </param>
        public void RequestAutoDjState(StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                if (!_api.Player_GetAutoDjEnabled())
                {
                    _api.Player_StartAutoDj();
                }
                else
                {
                    _api.Player_EndAutoDj();
                }
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerAutoDj,
                        _api.Player_GetAutoDjEnabled()).ToJsonString()));
        }

        /// <summary>
        /// This function is used to change the playing track's last.fm love rating.
        /// </summary>
        /// <param name="action">
        ///     The action can be either love, or ban.
        /// </param>
        /// <param name="clientId"></param>
        public void RequestLoveStatus(string action, string clientId)
        {
            var hwnd = _api.MB_GetWindowHandle();
            var mb = (Form) Control.FromHandle(hwnd);

            if (action.Equals("toggle", StringComparison.OrdinalIgnoreCase))
            {
                if (GetLfmStatus() == LastfmStatus.Love || GetLfmStatus() == LastfmStatus.Ban)
                {
                    mb.Invoke(new MethodInvoker(SetLfmNormalStatus));
                }
                else
                {
                    mb.Invoke(new MethodInvoker(SetLfmLoveStatus));
                }
            }
            else if (action.Equals("love", StringComparison.OrdinalIgnoreCase))
            {
                mb.Invoke(new MethodInvoker(SetLfmLoveStatus));
            }
            else if (action.Equals("ban", StringComparison.OrdinalIgnoreCase))
            {
                mb.Invoke(new MethodInvoker(SetLfmLoveBan));
            }
            else
            {
                SendLfmStatusMessage(GetLfmStatus());
            }
        }

        private void SetLfmNormalStatus()
        {
            var fileUrl = _api.NowPlaying_GetFileUrl();
            var success = _api.Library_SetFileTag(fileUrl, MetaDataType.RatingLove, "lfm");
            if (success)
            {
                SendLfmStatusMessage(LastfmStatus.Normal);
            }
        }

        private static void SendLfmStatusMessage(LastfmStatus lastfmStatus)
        {
            var data = new SocketMessage(Constants.NowPlayingLfmRating, lastfmStatus).ToJsonString();
            EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, data));
        }

        private void SetLfmLoveStatus()
        {
            var fileUrl = _api.NowPlaying_GetFileUrl();
            var success = _api.Library_SetFileTag(fileUrl, MetaDataType.RatingLove, "Llfm");
            if (success)
            {
                SendLfmStatusMessage(LastfmStatus.Love);
            }
        }

        private void SetLfmLoveBan()
        {
            var fileUrl = _api.NowPlaying_GetFileUrl();
            var success = _api.Library_SetFileTag(fileUrl, MetaDataType.RatingLove, "Blfm");
            if (success)
            {
                SendLfmStatusMessage(LastfmStatus.Ban);
            }
        }

        private LastfmStatus GetLfmStatus()
        {
            LastfmStatus lastfmStatus;
            var apiReply = _api.NowPlaying_GetFileTag(MetaDataType.RatingLove);
            if (apiReply.Equals("L") || apiReply.Equals("lfm") || apiReply.Equals("Llfm"))
            {
                lastfmStatus = LastfmStatus.Love;
            }
            else if (apiReply.Equals("B") || apiReply.Equals("Blfm"))
            {
                lastfmStatus = LastfmStatus.Ban;
            }
            else
            {
                lastfmStatus = LastfmStatus.Normal;
            }

            return lastfmStatus;
        }

        /// <summary>
        /// The function checks the MusicBee api and gets all the available playlist urls.
        /// </summary>
        /// <param name="clientId"></param>
        public void GetAvailablePlaylistUrls(string clientId)
        {
            _api.Playlist_QueryPlaylists();
            var playlists = new List<Playlist>();
            while (true)
            {
                var url = _api.Playlist_QueryGetNextPlaylist();

                if (string.IsNullOrEmpty(url))
                {
                    break;
                }

                var name = _api.Playlist_GetName(url);

                var playlist = new Playlist
                {
                    Name = name,
                    Url = url
                };
                playlists.Add(playlist);
            }

            var data = new SocketMessage(Constants.PlaylistList, playlists).ToJsonString();
            EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, data, clientId));
        }

        public void PlayPlaylist(string clientId, string url)
        {
            var success = _api.Playlist_PlayNow(url);
            var data = new SocketMessage(Constants.PlaylistPlay, success).ToJsonString();
            EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, data, clientId));
        }

        public void LibraryPlayAll(string clientId, bool shuffle = false)
        {
            bool success;

            if (shuffle)
            {
                success = _api.NowPlayingList_PlayLibraryShuffled();
            }
            else
            {
                if (_api.Player_GetAutoDjEnabled())
                {
                    _api.Player_EndAutoDj();
                }

                _api.Player_SetShuffle(false);

                string[] songsList = null;
                _api.Library_QueryFilesEx(null, ref songsList);
                if (songsList.Length > 0)
                {
                    _api.NowPlayingList_Clear();
                    success = _api.NowPlayingList_QueueFilesNext(songsList);
                    _api.Player_PlayNextTrack();
                }
                else
                {
                    success = false;
                }
            }

            var data = new SocketMessage(Constants.LibraryPlayAll, success).ToJsonString();
            EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, data, clientId));
        }

        /// <summary>
        ///
        /// </summary>ea
        /// <param name="clientId"></param>
        public void RequestPlayerStatus(string clientId)
        {
            var status = new Dictionary<string, object>
            {
                [Constants.PlayerRepeat] = _api.Player_GetRepeat().ToString(),
                [Constants.PlayerMute] = _api.Player_GetMute(),
                [Constants.PlayerShuffle] = Authenticator.ClientProtocolMisMatch(clientId)
                    ? (object) _api.Player_GetShuffle()
                    : GetShuffleState(),
                [Constants.PlayerScrobble] = _api.Player_GetScrobbleEnabled(),
                [Constants.PlayerState] = _api.Player_GetPlayState().ToString(),
                [Constants.PlayerVolume] = ((int) Math.Round(_api.Player_GetVolume() * 100, 1)).ToString(
                    CultureInfo.InvariantCulture)
            };

            var data = new SocketMessage(Constants.PlayerStatus, status).ToJsonString();
            EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, data, clientId));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="clientId"></param>
        public void RequestTrackInfo(string clientId)
        {
            var protocolVersion = Authenticator.ClientProtocolVersion(clientId);
            var message = protocolVersion > 2
                ? new SocketMessage(Constants.NowPlayingTrack, GetTrackInfoV2())
                : new SocketMessage(Constants.NowPlayingTrack, GetTrackInfo());

            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        public void RequestTrackDetails(string clientId)
        {
            var message = new SocketMessage(Constants.NowPlayingDetails, GetPlayingTrackDetails());

            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        public void SetTrackTag(string tagName, string value, string clientId)
        {
            var currentTrack = _api.NowPlaying_GetFileUrl();
            var metadataType = MetaDataType.AlbumArtistRaw;
            switch (tagName)
            {
                case "TrackTitle":
                    metadataType = MetaDataType.TrackTitle;
                    break;
                case "Artist":
                    metadataType = MetaDataType.Artist;
                    break;
                case "Album":
                    metadataType = MetaDataType.Album;
                    break;
                case "Year":
                    metadataType = MetaDataType.Year;
                    break;
                case "AlbumArtist":
                    metadataType = MetaDataType.AlbumArtist;
                    break;
                case "Genre":
                    metadataType = MetaDataType.Genre;
                    break;
                case "TrackNo":
                    metadataType = MetaDataType.TrackNo;
                    break;
                case "TrackCount":
                    metadataType = MetaDataType.TrackCount;
                    break;
                case "DiscNo":
                    metadataType = MetaDataType.DiscNo;
                    break;
                case "DiscCount":
                    metadataType = MetaDataType.DiscCount;
                    break;
                case "Grouping":
                    metadataType = MetaDataType.Grouping;
                    break;
                case "Publisher":
                    metadataType = MetaDataType.Publisher;
                    break;
                case "RatingAlbum":
                    metadataType = MetaDataType.RatingAlbum;
                    break;
                case "Composer":
                    metadataType = MetaDataType.Composer;
                    break;
                case "Comment":
                    metadataType = MetaDataType.Comment;
                    break;
                case "Encoder":
                    metadataType = MetaDataType.Encoder;
                    break;
                case "Lyrics":
                    metadataType = MetaDataType.Lyrics;
                    break;
            }

            if (metadataType == MetaDataType.AlbumArtistRaw)
            {
                _logger.Error("Requested tag type not found");
                return;
            }

            try
            {
                _api.Library_SetFileTag(currentTrack, metadataType, value);
                _api.Library_CommitTagsToFile(currentTrack);
                _api.MB_RefreshPanels();

                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingDetails, GetPlayingTrackDetails())));
            }
            catch (Exception e)
            {
                _logger.Error(e, "On Tag Change call");
            }
        }

        /// <summary>
        /// Moves a track of the now playing list to a new position.
        /// </summary>
        /// <param name="clientId">The Id of the client that initiated the request</param>
        /// <param name="from">The initial position</param>
        /// <param name="to">The final position</param>
        public void RequestNowPlayingMove(string clientId, int from, int to)
        {
            bool result;
            int[] aFrom = {from};
            int dIn;
            if (from > to)
            {
                dIn = to - 1;
            }
            else
            {
                dIn = to;
            }

            result = _api.NowPlayingList_MoveFiles(aFrom, dIn);

            var reply = new
            {
                success = result,
                from,
                to
            };
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListMove, reply).ToJsonString(), clientId));
        }

        private static string XmlFilter(string[] tags, string query, bool isStrict,
            SearchSource source = SearchSource.None)
        {
            short src;
            if (source != SearchSource.None)
            {
                src = (short) source;
            }
            else
            {
                var userDefaults = UserSettings.Instance.Source != SearchSource.None;
                src = (short)
                    (userDefaults
                        ? UserSettings.Instance.Source
                        : SearchSource.Library);
            }


            var filter = new XElement("Source",
                new XAttribute("Type", src));

            var conditions = new XElement("Conditions",
                new XAttribute("CombineMethod", "Any"));
            foreach (var tag in tags)
            {
                var condition = new XElement("Condition",
                    new XAttribute("Field", tag),
                    new XAttribute("Comparison", isStrict ? "Is" : "Contains"),
                    new XAttribute("Value", query));
                conditions.Add(condition);
            }

            filter.Add(conditions);

            return filter.ToString();
        }

        /// <summary>
        /// Calls the API to get albums matching the specified parameter. Fires an event with the message in JSON format.
        /// </summary>
        /// <param name="albumName">Is used to filter through the data.</param>
        /// <param name="clientId">The client that initiated the call. (Should also be the only one to receive the data.</param>
        public void LibrarySearchAlbums(string albumName, string clientId)
        {
            var filter = XmlFilter(new[] {"Album"}, albumName, false);

            var albums = new List<Album>();

            if (_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", filter))
            {
                try
                {
                    foreach (
                        var entry in
                        new List<string>(_api.Library_QueryGetLookupTableValue(null)
                            .Split(new[] {"\0\0"}, StringSplitOptions.None)))
                    {
                        if (string.IsNullOrEmpty(entry)) continue;
                        var albumInfo = entry.Split('\0');
                        if (albumInfo.Length < 2) continue;

                        var current = albumInfo.Length == 3
                            ? new Album(albumInfo[1], albumInfo[2])
                            : new Album(albumInfo[0], albumInfo[1]);
                        if (current.album.IndexOf(albumName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        if (!albums.Contains(current))
                        {
                            albums.Add(current);
                        }
                        else
                        {
                            albums.ElementAt(albums.IndexOf(current)).IncreaseCount();
                        }
                    }
                }
                catch (IndexOutOfRangeException)
                {
                }
            }

            _api.Library_QueryLookupTable(null, null, null);

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchAlbum,
                        albums).ToJsonString(), clientId));
        }

        /// <summary>
        /// Used to get all the albums by the specified artist.
        /// </summary>
        /// <param name="artist"></param>
        /// <param name="clientId"></param>
        public void LibraryGetArtistAlbums(string artist, string clientId)
        {
            var albumList = new List<Album>();
            if (_api.Library_QueryFiles(XmlFilter(new[] {"ArtistPeople"}, artist, true)))
            {
                while (true)
                {
                    var currentFile = _api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentFile)) break;
                    var current = new Album(_api.Library_GetFileTag(currentFile, MetaDataType.AlbumArtist),
                        _api.Library_GetFileTag(currentFile, MetaDataType.Album));
                    if (!albumList.Contains(current))
                    {
                        albumList.Add(current);
                    }
                    else
                    {
                        albumList.ElementAt(albumList.IndexOf(current)).IncreaseCount();
                    }
                }
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibraryArtistAlbums,
                        albumList).ToJsonString(), clientId));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="artist"></param>
        /// <param name="clientId"></param>
        public void LibrarySearchArtist(string artist, string clientId)
        {
            var artistList = new List<Artist>();

            if (_api.Library_QueryLookupTable("artist", "count",
                XmlFilter(new[] {"ArtistPeople"}, artist, false)))
            {
                artistList.AddRange(_api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split('\0'))
                    .Select(artistInfo => new Artist(artistInfo[0], int.Parse(artistInfo[1]))));
            }

            _api.Library_QueryLookupTable(null, null, null);

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchArtist,
                        artistList).ToJsonString(), clientId));
        }


        /// <summary>
        ///
        /// </summary>
        /// <param name="genre"></param>
        /// <param name="clientId"></param>
        public void LibraryGetGenreArtists(string genre, string clientId)
        {
            var artistList = new List<Artist>();

            if (_api.Library_QueryLookupTable("artist", "count", XmlFilter(new[] {"Genre"}, genre, true)))
            {
                artistList.AddRange(_api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split('\0'))
                    .Select(artistInfo => new Artist(artistInfo[0], int.Parse(artistInfo[1]))));
            }

            _api.Library_QueryLookupTable(null, null, null);
            var message = new SocketMessage(Constants.LibraryGenreArtists, artistList).ToJsonString();
            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message, clientId);
            EventBus.FireEvent(messageEvent);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="genre"></param>
        /// <param name="clientId"></param>
        public void LibrarySearchGenres(string genre, string clientId)
        {
            var genreList = new List<Genre>();
            var query = XmlFilter(new[] {"Genre"}, genre, false);
            if (_api.Library_QueryLookupTable("genre", "count", query))
            {
                genreList.AddRange(_api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split(new[] {'\0'}, StringSplitOptions.None))
                    .Select(genreInfo => new Genre(genreInfo[0], int.Parse(genreInfo[1]))));
            }

            _api.Library_QueryLookupTable(null, null, null);

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchGenre,
                        genreList).ToJsonString(), clientId));
        }

        public void LibraryBrowseGenres(string clientId, int offset = 0, int limit = 4000)
        {
            var genres = new List<Genre>();
            if (_api.Library_QueryLookupTable("genre", "count", null))
            {
                genres.AddRange(_api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split(new[] {'\0'}, StringSplitOptions.None))
                    .Select(genreInfo => new Genre(genreInfo[0].Cleanup(), int.Parse(genreInfo[1]))));
            }

            _api.Library_QueryLookupTable(null, null, null);

            var total = genres.Count;
            var realLimit = offset + limit > total ? total - offset : limit;

            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseGenres,
                Data = new Page<Genre>
                {
                    Data = offset > total ? new List<Genre>() : genres.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };

            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        public void LibraryBrowseArtists(string clientId, int offset = 0, int limit = 4000, bool albumArtists = false)
        {
            var artists = new List<Artist>();
            var artistType = "artist";
            if (albumArtists)
            {
                artistType = "albumartist";
            }

            if (_api.Library_QueryLookupTable(artistType, "count", null))
            {
                artists.AddRange(_api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split('\0'))
                    .Select(artistInfo => new Artist(artistInfo[0].Cleanup(), int.Parse(artistInfo[1]))));
            }

            _api.Library_QueryLookupTable(null, null, null);
            var total = artists.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseArtists,
                Data = new Page<Artist>
                {
                    Data = offset > total ? new List<Artist>() : artists.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };

            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        private static Album CreateAlbum(string queryResult)
        {
            var albumInfo = queryResult.Split('\0');

            albumInfo = albumInfo.Select(s => s.Cleanup()).ToArray();

            if (albumInfo.Length == 1)
            {
                return new Album(albumInfo[0], string.Empty);
            }

            if (albumInfo.Length == 2 && queryResult.StartsWith("\0"))
            {
                return new Album(albumInfo[1], string.Empty);
            }

            var current = albumInfo.Length == 3
                ? new Album(albumInfo[1], albumInfo[2])
                : new Album(albumInfo[0], albumInfo[1]);

            return current;
        }

        public void LibraryBrowseAlbums(string clientId, int offset = 0, int limit = 4000)
        {
            var albums = new List<Album>();

            if (_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", null))
            {
                try
                {
                    List<Album> data;

                    if (Utilities.IsAndroid(clientId))
                    {
                        data = _api.Library_QueryGetLookupTableValue(null)
                            .Split(new[] { "\0\0" }, StringSplitOptions.None)
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Select(s => s.Trim())
                            .Select(CreateAlbum)
                            .Distinct()
                            .ToList();
                    }
                    else
                    {
                        data = _api.Library_QueryGetLookupTableValue(null)
                            .Split(new[] { "\0\0" }, StringSplitOptions.None)
                            .Select(s => s.Trim())
                            .Select(CreateAlbum)
                            .Distinct()
                            .ToList();
                    }

                    albums.AddRange(data);
                }
                catch (IndexOutOfRangeException ex)
                {
                    _logger.Error(ex, "While loading album data");
                }
            }

            _api.Library_QueryLookupTable(null, null, null);

            var total = albums.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseAlbums,
                Data = new Page<Album>
                {
                    Data = offset > total ? new List<Album>() : albums.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };

            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        public void LibraryBrowseTracks(string clientId, int offset = 0, int limit = 4000)
        {
            var tracks = new List<Track>();
            if (_api.Library_QueryFiles(null))
            {
                while (true)
                {
                    var currentTrack = _api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack)) break;

                    int trackNumber;
                    int discNumber;

                    int.TryParse(_api.Library_GetFileTag(currentTrack, MetaDataType.TrackNo), out trackNumber);
                    int.TryParse(_api.Library_GetFileTag(currentTrack, MetaDataType.DiscNo), out discNumber);

                    var track = new Track
                    {
                        Artist = GetArtistForTrack(currentTrack),
                        Title = GetTitleForTrack(currentTrack),
                        Album = GetAlbumForTrack(currentTrack),
                        AlbumArtist = GetAlbumArtistForTrack(currentTrack),
                        Genre = GetGenreForTrack(currentTrack),
                        Disc = discNumber,
                        Trackno = trackNumber,
                        Src = currentTrack,
                    };
                    tracks.Add(track);
                }
            }

            var total = tracks.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseTracks,
                Data = new Page<Track>
                {
                    Data = offset > total ? new List<Track>() : tracks.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };

            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        private string GetGenreForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, MetaDataType.Genre).Cleanup();
        }

        private string GetAlbumArtistForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, MetaDataType.AlbumArtist).Cleanup();
        }

        private string GetAlbumForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, MetaDataType.Album).Cleanup();
        }

        private string GetTitleForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle).Cleanup();
        }

        private string GetArtistForTrack(string currentTrack)
        {
            return _api.Library_GetFileTag(currentTrack, MetaDataType.Artist).Cleanup();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="title"></param>
        /// <param name="clientId"></param>
        public void LibrarySearchTitle(string title, string clientId)
        {
            var tracks = new List<Track>();
            if (_api.Library_QueryFiles(XmlFilter(new[] {"Title"}, title, false)))
            {
                while (true)
                {
                    var currentTrack = _api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack)) break;

                    int trackNumber;
                    int.TryParse(_api.Library_GetFileTag(currentTrack, MetaDataType.TrackNo), out trackNumber);
                    var src = currentTrack;

                    tracks.Add(new Track(_api.Library_GetFileTag(currentTrack, MetaDataType.Artist),
                        _api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle),
                        trackNumber, src));
                }
            }

            _api.Library_QueryLookupTable(null, null, null);
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchTitle, tracks).ToJsonString(), clientId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="album"></param>
        /// <param name="client"></param>
        public void LibraryGetAlbumTracks(string album, string client)
        {
            var trackList = new List<Track>();
            if (_api.Library_QueryFiles(XmlFilter(new[] {"Album"}, album, true)))
            {
                while (true)
                {
                    var currentTrack = _api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack)) break;

                    int.TryParse(_api.Library_GetFileTag(currentTrack, MetaDataType.TrackNo), out var trackNumber);
                    int.TryParse(_api.Library_GetFileTag(currentTrack, MetaDataType.DiscNo), out var discNumber);
                    var src = currentTrack;

                    trackList.Add(new Track
                    {
                        Artist = _api.Library_GetFileTag(currentTrack, MetaDataType.Artist),
                        Title = _api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle),
                        AlbumArtist = _api.Library_GetFileTag(currentTrack, MetaDataType.AlbumArtist),
                        Disc = discNumber,
                        Trackno = trackNumber,
                        Src = src,
                    });
                }
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibraryAlbumTracks, trackList).ToJsonString(), client));
        }

        public void RequestRadioStations(string clientId, int offset = 0, int limit = 4000)
        {
            var radioStations = new string[] { };
            var success = _api.Library_QueryFilesEx("domain=Radio", ref radioStations);
            List<RadioStation> stations;
            if (success)
            {
                stations = radioStations.Select(s => new RadioStation
                    {
                        Url = s,
                        Name = _api.Library_GetFileTag(s, MetaDataType.TrackTitle)
                    })
                    .ToList();
            }
            else
            {
                stations = new List<RadioStation>();
            }

            var total = stations.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.RadioStations,
                Data = new Page<RadioStation>
                {
                    Data = offset > total ? new List<RadioStation>() : stations.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };

            SendReply(message.ToJsonString(), clientId);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queue"></param>
        /// <param name="tag"></param>
        /// <param name="query"></param>
        public void RequestQueueFiles(QueueType queue, MetaTag tag, string query)
        {
            string[] trackList;
            if (tag == MetaTag.title && queue == QueueType.PlayNow)
            {
                trackList = new[] {query};
            }
            else
            {
                trackList = GetUrlsForTag(tag, query);
            }

            QueueFiles(queue, trackList, query);
        }

        /// <summary>
        /// Takes a given query string and searches the Now Playing list for any track with a matching title or artist.
        /// The title is checked first.
        /// </summary>
        /// <param name="query">The string representing the query</param>
        /// <param name="clientId">Client</param>
        public void NowPlayingSearch(string query, string clientId)
        {
            var result = false;
            _api.NowPlayingList_QueryFiles(XmlFilter(new[] {"ArtistPeople", "Title"}, query, false));

            while (true)
            {
                var currentTrack = _api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(currentTrack)) break;
                var artist = _api.Library_GetFileTag(currentTrack, MetaDataType.Artist);
                var title = _api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle);

                if (title.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                result = _api.NowPlayingList_PlayNow(currentTrack);
                break;
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListSearch, result).ToJsonString(), clientId));
        }

        public string[] GetUrlsForTag(MetaTag tag, string query)
        {
            var filter = string.Empty;
            string[] tracks = { };
            switch (tag)
            {
                case MetaTag.artist:
                    filter = XmlFilter(new[] {"ArtistPeople"}, query, true);
                    break;
                case MetaTag.album:
                    filter = XmlFilter(new[] {"Album"}, query, true);
                    break;
                case MetaTag.genre:
                    filter = XmlFilter(new[] {"Genre"}, query, true);
                    break;
                case MetaTag.title:
                    filter = "";
                    break;
            }


            _api.Library_QueryFilesEx(filter, ref tracks);

            var list = tracks.Select(file => new MetaData
                {
                    file = file,
                    artist = _api.Library_GetFileTag(file, MetaDataType.Artist),
                    album_artist = _api.Library_GetFileTag(file, MetaDataType.AlbumArtist),
                    album = _api.Library_GetFileTag(file, MetaDataType.Album),
                    title = _api.Library_GetFileTag(file, MetaDataType.TrackTitle),
                    genre = _api.Library_GetFileTag(file, MetaDataType.Genre),
                    year = _api.Library_GetFileTag(file, MetaDataType.Year),
                    track_no = _api.Library_GetFileTag(file, MetaDataType.TrackNo),
                    disc = _api.Library_GetFileTag(file, MetaDataType.DiscNo)
                })
                .ToList();
            list.Sort();
            tracks = list.Select(r => r.file)
                .ToArray();

            return tracks;
        }

        public void RequestPlay(string clientId)
        {
            var state = _api.Player_GetPlayState();

            if (state != PlayState.Playing)
            {
                _api.Player_PlayPause();
            }
        }

        public void RequestPausePlayback(string clientId)
        {
            var state = _api.Player_GetPlayState();

            if (state == PlayState.Playing)
            {
                _api.Player_PlayPause();
            }
        }

        /// <summary>
        ///     Initializes the logging configuration.
        /// </summary>
        /// <param name="logFilePath"></param>
        public static void InitializeLoggingConfiguration(string logFilePath, LogLevel logLevel)
        {
            var config = new LoggingConfiguration();

            var fileTarget = new FileTarget
            {
                ArchiveAboveSize = 2097152,
                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 5,
                EnableArchiveFileCompression = true,
                FileName = logFilePath,
                Layout = "${longdate} [${level:uppercase=true}]${newline}" +
                         "${logger} : ${callsite-linenumber} ${when:when=length('${threadname}') > 0: [${threadname}]}${newline}" +
                         "${message}${newline}" +
                         "${when:when=length('${exception}') > 0: ${exception}${newline}}"
            };


#if DEBUG
            var consoleTarget = new ColoredConsoleTarget();
            var debugger = new DebuggerTarget();
            var sentinalTarget = new NLogViewerTarget()
            {
                Name = "sentinel",
                Address = "udp://127.0.0.1:9999",
                IncludeNLogData = true,
                IncludeSourceInfo = true
            };

            var sentinelRule = new LoggingRule("*", LogLevel.Trace, sentinalTarget);
            config.AddTarget("sentinel", sentinalTarget);
            config.LoggingRules.Add(sentinelRule);
            config.AddTarget("console", consoleTarget);
            config.AddTarget("debugger", debugger);
            consoleTarget.Layout = @"${date:format=HH\\:MM\\:ss} ${logger} ${message} ${exception}";

            debugger.Layout = fileTarget.Layout;

            var consoleRule = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(consoleRule);

            var debuggerRule = new LoggingRule("*", LogLevel.Debug, debugger);
            config.LoggingRules.Add(debuggerRule);
#endif
            config.AddTarget("file", fileTarget);

            var fileRule = new LoggingRule("*", logLevel, fileTarget);

            config.LoggingRules.Add(fileRule);

            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }

        public void SelectionChanged(bool enabled)
        {
            InitializeLoggingConfiguration(UserSettings.Instance.FullLogPath,
                enabled ? LogLevel.Debug : LogLevel.Error);
        }

        /// <summary>
        /// Gets a Page of playlists from the plugin api and sends it to the client that requested it.
        /// </summary>
        /// <param name="clientId">The id of the client performing the request</param>
        /// <param name="offset">The starting position (zero based) of the dataset</param>
        /// <param name="limit">The number of elements in the dataset</param>
        public void GetAvailablePlaylistUrls(string clientId, int offset, int limit)
        {
            _api.Playlist_QueryPlaylists();
            var playlists = new List<Playlist>();
            while (true)
            {
                var url = _api.Playlist_QueryGetNextPlaylist();

                if (string.IsNullOrEmpty(url))
                {
                    break;
                }

                var name = _api.Playlist_GetName(url);

                var playlist = new Playlist
                {
                    Name = name,
                    Url = url
                };
                playlists.Add(playlist);
            }

            var total = playlists.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.PlaylistList,
                Data = new Page<Playlist>
                {
                    Data = offset > total ? new List<Playlist>() : playlists.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };
            var messageEvent = new MessageEvent(EventType.ReplyAvailable, message.ToJsonString(), clientId);
            EventBus.FireEvent(messageEvent);
        }

        public bool QueueFiles(QueueType queue, string[] data, string query = "")
        {
            switch (queue)
            {
                case QueueType.Next:
                    return _api.NowPlayingList_QueueFilesNext(data);
                case QueueType.Last:
                    return _api.NowPlayingList_QueueFilesLast(data);
                case QueueType.PlayNow:
                    _api.NowPlayingList_Clear();
                    _api.NowPlayingList_QueueFilesLast(data);
                    return _api.NowPlayingList_PlayNow(data[0]);
                case QueueType.AddAndPlay:
                    _api.NowPlayingList_Clear();
                    _api.NowPlayingList_QueueFilesLast(data);
                    return _api.NowPlayingList_PlayNow(query);
                default:
                    return false;
            }
        }

        public void SwitchOutputDevice(string outputDevice, string clientId)
        {
            _api.Player_SetOutputDevice(outputDevice);
            RequestOutputDevice(clientId);
        }

        private string CacheCover(string track)
        {
            var locations = PictureLocations.EmbedInFile |
                            PictureLocations.LinkToSource |
                            PictureLocations.LinkToOrganisedCopy;

            var pictureUrl = string.Empty;
            var data = new byte[] { };

            _api.Library_GetArtworkEx(
                track,
                0,
                true,
                ref locations,
                ref pictureUrl,
                ref data
            );
            

            if (!pictureUrl.IsNullOrEmpty())
            {
                return Utilities.StoreCoverToCache(pictureUrl);
            }

            var hash = data?.Length > 0 ? Utilities.StoreCoverToCache(data) : string.Empty;
            return hash;
        }

        private void PrepareCache()
        {
            var watch = Stopwatch.StartNew();
            var identifiers = GetIdentifiers();

            _logger.Debug($"Detected {identifiers.Count} albums");

            _api.Library_QueryLookupTable(null, null, null);

            if (!_api.Library_QueryFiles(null))
            {
                _logger.Debug($"No result in query");
                return;
            }

            var paths = new Dictionary<string, string>();
            var modified = new Dictionary<string, string>();

            while (true)
            {
                var currentTrack = _api.Library_QueryGetNextFile();
                if (string.IsNullOrEmpty(currentTrack)) break;

                var album = GetAlbumForTrack(currentTrack);
                var artist = GetAlbumArtistForTrack(currentTrack);
                var fileModified = _api.Library_GetFileProperty(currentTrack, FilePropertyType.DateModified);

                try
                {
                    var key = Utilities.CoverIdentifier(artist, album);

                    if (!identifiers.Contains((key)))
                    {
                        continue;
                    }

                    paths[key] = currentTrack;
                    modified[key] = fileModified;
                }
                catch (Exception e)
                {
                    _logger.Error(e, $"Failed creating identifier for {album} by {artist}");
                }
            }

            CoverCache.Instance.WarmUpCache(paths, modified);
            watch.Stop();
            _logger.Debug($"Cover cache preparation: {watch.ElapsedMilliseconds} ms");
        }

        private void BuildCoverCache()
        {
            var watch = Stopwatch.StartNew();
            CoverCache.Instance.Build(CacheCover);
            watch.Stop();
            _logger.Debug($"Cover cache task complete after: {watch.ElapsedMilliseconds} ms");
        }

        private List<string> GetIdentifiers()
        {
            var identifiers = new List<string>();
            if (!_api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", null)) return identifiers;
            try
            {
                var data = _api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s.Trim())
                    .Select(CreateAlbum)
                    .Select(album => Utilities.CoverIdentifier(album.artist, album.album))
                    .Distinct()
                    .ToList();

                identifiers.AddRange(data);
            }
            catch (IndexOutOfRangeException ex)
            {
                _logger.Error(ex, "While loading album data");
            }

            return identifiers;
        }

        private AlbumCoverPayload GetCoverSize(string artist, string album, string size)
        {
            AlbumCoverPayload payload;
            var isNumeric = int.TryParse(size, out var coverSize);
            var original = size == "original";
            if (!original && !isNumeric)
            {
                payload = GetAlbumCoverStatusPayload(400);
            }
            else
            {
                var key = Utilities.CoverIdentifier(artist, album);
                var path = CoverCache.Instance.Lookup(key);
                if (path.IsNullOrEmpty())
                {
                    payload = GetAlbumCoverStatusPayload(404);
                }
                else
                {
                    var locations = PictureLocations.EmbedInFile |
                                    PictureLocations.LinkToSource |
                                    PictureLocations.LinkToOrganisedCopy;

                    var pictureUrl = string.Empty;
                    var data = new byte[] { };

                    _api.Library_GetArtworkEx(
                        path,
                        0,
                        true,
                        ref locations,
                        ref pictureUrl,
                        ref data
                    );

                    if (!pictureUrl.IsNullOrEmpty())
                    {
                        payload = new AlbumCoverPayload
                        {
                            Cover = original
                                ? Utilities.FileToBase64(pictureUrl)
                                : Utilities.ImageResizeFile(pictureUrl, coverSize, coverSize),
                            Status = 200,
                            Hash = Utilities.Sha1HashFile(pictureUrl)
                        };
                    }
                    else if (data?.Length > 0)
                    {
                        payload = new AlbumCoverPayload
                        {
                            Cover = original
                                ? Convert.ToBase64String(data)
                                : Utilities.ImageResize(data, coverSize, coverSize),
                            Status = 200,
                            Hash = Utilities.Sha1Hash(data)
                        };
                    }
                    else
                    {
                        payload = GetAlbumCoverStatusPayload(404);
                    }
                }
            }

            return payload;
        }

        public void RequestCover(string clientId, string artist, string album, string clientHash, string size)
        {
            var stopwatch = Stopwatch.StartNew();
            AlbumCoverPayload payload;

            try
            {
                if (artist == null)
                {
                    payload = GetAlbumCoverStatusPayload(500);
                }
                else if (album.IsNullOrEmpty())
                {
                    payload = GetAlbumCoverStatusPayload(400);
                }
                else
                {
                    payload = size != null
                        ? GetCoverSize(artist, album, size)
                        : GetAlbumCover(artist, album, clientHash);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e);
                payload = GetAlbumCoverStatusPayload(500);
            }

            var message = new SocketMessage(Constants.LibraryAlbumCover, payload);
            SendReply(message.ToJsonString(), clientId);
            stopwatch.Stop();
            _logger.Debug($"cover request took {stopwatch.Elapsed} ms");
        }

        private AlbumCoverPayload GetAlbumCover(string artist, string album, string clientHash)
        {
            var cache = CoverCache.Instance;
            var key = Utilities.CoverIdentifier(artist, album);
            string hash;
            if (cache.IsCached(key))
            {
                hash = cache.GetCoverHash(key);
                return hash.IsEmpty() ? GetAlbumCoverStatusPayload(404) : GetAlbumCoverFromCache(clientHash, hash);
            }

            var path = cache.Lookup(key);
            if (path.IsNullOrEmpty())
            {
                return GetAlbumCoverStatusPayload(404);
            }

            hash = CacheCover(path);
            if (hash.IsEmpty())
            {
                return GetAlbumCoverStatusPayload(404);
            }


            cache.Update(key, hash);
            return GetAlbumCoverFromCache(clientHash, hash);
        }

        private static AlbumCoverPayload GetAlbumCoverStatusPayload(int status)
        {
            return new AlbumCoverPayload
            {
                Status = status
            };
        }

        private static AlbumCoverPayload GetAlbumCoverFromCache(string clientHash, string hash)
        {
            if (clientHash.IsEmpty() || clientHash != hash)
            {
                return new AlbumCoverPayload
                {
                    Cover = Utilities.GetCoverFromCache(hash),
                    Status = 200,
                    Hash = hash
                };
            }

            return GetAlbumCoverStatusPayload(304);
        }

        public void RequestCoverPage(string clientId, int offset, int limit)
        {
            var stopwatch = Stopwatch.StartNew();
            var keys = CoverCache.Instance.Keys();
            var page = keys.Skip(offset).Take(limit);

            var data = (from key in page
                let hash = CoverCache.Instance.GetCoverHash(key)
                let path = CoverCache.Instance.Lookup(key)
                let cover = Utilities.GetCoverFromCache(hash)
                select new AlbumCoverPayload
                {
                    Artist = GetAlbumArtistForTrack(path),
                    Album = GetAlbumForTrack(path),
                    Cover = cover,
                    Hash = hash,
                    Status = cover.IsEmpty() ? 404 : 200
                }).ToList();

            var message = new SocketMessage
            {
                Context = Constants.LibraryAlbumCover,
                Data = new Page<AlbumCoverPayload>
                {
                    Data = offset > keys.Count ? new List<AlbumCoverPayload>() : data,
                    Offset = offset,
                    Limit = limit,
                    Total = keys.Count
                }
            };

            SendReply(message.ToJsonString(), clientId);
            stopwatch.Stop();
            _logger.Debug($"cover page from: {offset} with {limit} limit, request took {stopwatch.Elapsed} ms");
        }
    }
}