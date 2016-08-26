using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using System.Windows.Forms;
using System.Xml.Linq;
using MusicBeePlugin.AndroidRemote;
using MusicBeePlugin.AndroidRemote.Controller;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.AndroidRemote.Utilities;
using NLog;
using NLog.Config;
using NLog.Targets;
using ServiceStack.Text;
using Timer = System.Timers.Timer;

namespace MusicBeePlugin
{
    /// <summary>
    /// The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
    public partial class Plugin
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// The mb api interface.
        /// </summary>
        private MusicBeeApiInterface api;

        /// <summary>
        /// The _about.
        /// </summary>
        private readonly PluginInfo about = new PluginInfo();

        /// <summary>
        /// The timer.
        /// </summary>
        private Timer timer;

        private Timer positionUpdateTimer;

        /// <summary>
        /// The shuffle.
        /// </summary>
        private ShuffleState _shuffleState;

        /// <summary>
        /// Represents the current repeat mode.
        /// </summary>
        private RepeatMode repeat;

        /// <summary>
        /// The scrobble.
        /// </summary>
        private bool scrobble;

        /// <summary>
        /// Returns the plugin instance (Singleton);
        /// </summary>
        public static Plugin Instance
        {
            get { return selfInstance; }
        }

        private static Plugin selfInstance;
        private InfoWindow mWindow;
        private bool userChangingShuffle;


        /// <summary>
        /// This function initialized the Plugin.
        /// </summary>
        /// <param name="apiInterfacePtr"></param>
        /// <returns></returns>
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            selfInstance = this;
            JsConfig.ExcludeTypeInfo = true;
            Configuration.Register(Controller.Instance);

            api = new MusicBeeApiInterface();
            api.Initialise(apiInterfacePtr);

            UserSettings.Instance.SetStoragePath(api.Setting_GetPersistentStoragePath());
            UserSettings.Instance.LoadSettings();

            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "MusicBee Remote: Plugin";
            about.Description = "Remote Control for server to be used with android application.";
            about.Author = "Konstantinos Paparas (aka Kelsos)";
            about.TargetApplication = "MusicBee Remote";

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            UserSettings.Instance.CurrentVersion = version.ToString();

            // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            about.Type = PluginType.General;
            about.VersionMajor = Convert.ToInt16(version.Major);
            about.VersionMinor = Convert.ToInt16(version.Minor);
            about.Revision = Convert.ToInt16(version.Build);
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;

            if (api.ApiRevision < MinApiRevision)
            {
                return about;
            }

#if DEBUG
            InitializeLoggingConfiguration(api.Setting_GetPersistentStoragePath() + "\\" + "mb_remote", LogLevel.Debug);
#else
            InitializeLoggingConfiguration(api.Setting_GetPersistentStoragePath() + "\\" + "mb_remote", LogLevel.Error);
#endif
        

            StartPlayerStatusMonitoring();

            api.MB_AddMenuItem("mnuTools/MusicBee Remote", "Information Panel of the MusicBee Remote",
                MenuItemClicked);

            EventBus.FireEvent(new MessageEvent(EventType.ActionSocketStart));
            EventBus.FireEvent(new MessageEvent(EventType.InitializeModel));
            EventBus.FireEvent(new MessageEvent(EventType.StartServiceBroadcast));
            EventBus.FireEvent(new MessageEvent(EventType.ShowFirstRunDialog));

            positionUpdateTimer = new Timer(20000);
            positionUpdateTimer.Elapsed += PositionUpdateTimerOnElapsed;
            positionUpdateTimer.Enabled = true;

            return about;
        }

        private void PositionUpdateTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (api.Player_GetPlayState() == PlayState.Playing)
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
            if (mWindow != null && mWindow.Visible)
            {
                mWindow.UpdateSocketStatus(status);
            }
        }

        /// <summary>
        /// The function populates the local player status variables and then
        /// starts the Monitoring of the player status every 1000 ms to retrieve
        /// any changes.
        /// </summary>
        private void StartPlayerStatusMonitoring()
        {
            scrobble = api.Player_GetScrobbleEnabled();
            repeat = api.Player_GetRepeat();
            _shuffleState = GetShuffleState();
            timer = new Timer {Interval = 1000};
            timer.Elapsed += HandleTimerElapsed;
            timer.Enabled = true;
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
            if (GetShuffleState() != _shuffleState && !userChangingShuffle)
            {
                _shuffleState = GetShuffleState();
                EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, new SocketMessage(
                        Constants.PlayerShuffle, _shuffleState)
                    .ToJsonString()));
            }

            if (api.Player_GetScrobbleEnabled() != scrobble)
            {
                scrobble = api.Player_GetScrobbleEnabled();
                EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerScrobble, scrobble)
                        .ToJsonString()));
            }

            if (api.Player_GetRepeat() != repeat)
            {
                repeat = api.Player_GetRepeat();
                EventBus.FireEvent(new MessageEvent(
                    EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerRepeat, repeat).ToJsonString()));
            }
        }

        public void OpenInfoWindow()
        {
            IntPtr hwnd = api.MB_GetWindowHandle();
            Form MB = (Form) Form.FromHandle(hwnd);
            MB.Invoke(new MethodInvoker(DisplayInfoWindow));
        }

        private void DisplayInfoWindow()
        {
            if (mWindow == null || !mWindow.Visible)
            {
                mWindow = new InfoWindow();
            }

            mWindow.Show();
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
            string settingsFolder = api.Setting_GetPersistentStoragePath + "\\mb_remote";
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
                    RequestTrackRating(String.Empty, String.Empty);
                    RequestLoveStatus("status", "all");
                    RequestNowPlayingTrackLyrics();
                    RequestPlayPosition("status");
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingTrack, GetTrackInfo())
                            .ToJsonString()));
                    break;
                case NotificationType.VolumeLevelChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.PlayerVolume,
                        ((int)
                            Math.Round(
                                api.Player_GetVolume()*100,
                                1))).ToJsonString()));
                    break;
                case NotificationType.VolumeMuteChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.PlayerMute,
                            api.Player_GetMute()).ToJsonString()
                    ));
                    break;
                case NotificationType.PlayStateChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.PlayerState,
                                api
                                    .Player_GetPlayState
                                    ()).ToJsonString
                            ()));
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    if (api.ApiRevision >= 17)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.NowPlayingLyricsChange,
                            !String.IsNullOrEmpty(api.NowPlaying_GetDownloadedLyrics())
                                ? api.NowPlaying_GetDownloadedLyrics()
                                : "Lyrics Not Found"));
                    }
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    if (api.ApiRevision >= 17)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange,
                            api.NowPlaying_GetDownloadedArtwork()));
                    }
                    break;
                case NotificationType.NowPlayingListChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingListChanged, true).ToJsonString()));
                    break;
            }
        }

        private NowPlayingTrack GetTrackInfo()
        {
            NowPlayingTrack nowPlayingTrack = new NowPlayingTrack
            {
                Artist = api.NowPlaying_GetFileTag(MetaDataType.Artist),
                Album = api.NowPlaying_GetFileTag(MetaDataType.Album),
                Year = api.NowPlaying_GetFileTag(MetaDataType.Year)
            };
            nowPlayingTrack.SetTitle(api.NowPlaying_GetFileTag(MetaDataType.TrackTitle),
                api.NowPlaying_GetFileUrl());
            return nowPlayingTrack;
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
                        api.Player_PlayNextTrack()).ToJsonString()));
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
                        api.Player_Stop()).ToJsonString()));
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
                        api.Player_PlayPause()).ToJsonString()));
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
                        api.Player_PlayPreviousTrack()).ToJsonString()));
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
                api.Player_SetVolume((float) volume/100);
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerVolume,
                        ((int) Math.Round(api.Player_GetVolume()*100, 1))).ToJsonString()));

            if (api.Player_GetMute())
            {
                api.Player_SetMute(false);
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
                api.Player_SetShuffle(!api.Player_GetShuffle());
            }

            EventBus.FireEvent(
                new MessageEvent(
                    EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerShuffle,
                        api.Player_GetShuffle()).ToJsonString()));
        }

        /// <summary>
        /// Changes the player shuffle and autodj state following the model of MusicBee.
        /// </summary>
        /// <param name="action"></param>
        public void RequestAutoDjShuffleState(StateAction action)
        {
            var shuffleEnabled = api.Player_GetShuffle();
            var autoDjEnabled = api.Player_GetAutoDjEnabled();
            var shuffleState = ShuffleState.off;

            if (action == StateAction.Toggle)
            {
                if (shuffleEnabled && !autoDjEnabled)
                {
                    var success = api.Player_StartAutoDj();
                    if (success)
                    {
                        shuffleState = ShuffleState.autodj;
                    }
                }
                else if (autoDjEnabled)
                {
                    api.Player_EndAutoDj();
                }
                else
                {
                    var success = api.Player_SetShuffle(true);
                    if (success)
                    {
                        shuffleState = ShuffleState.shuffle;
                    }
                }
            }
            //  this.userChangingShuffle = true;

            //this._shuffleState = shuffleState;

//            var socketMessage = new SocketMessage(Constants.PlayerShuffle, shuffleState);
//            var messageEvent = new MessageEvent(EventType.ReplyAvailable, socketMessage.ToJsonString());
//            EventBus.FireEvent(messageEvent);
            // this.userChangingShuffle = false;
        }

        private ShuffleState GetShuffleState()
        {
            var shuffleEnabled = api.Player_GetShuffle();
            var autoDjEnabled = api.Player_GetAutoDjEnabled();
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
                api.Player_SetMute(!api.Player_GetMute());
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerMute,
                        api.Player_GetMute()).ToJsonString()));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="action"></param>
        public void RequestScrobblerState(StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                api.Player_SetScrobbleEnabled(!api.Player_GetScrobbleEnabled());
            }

            EventBus.FireEvent(
                new MessageEvent(
                    EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerScrobble,
                        api.Player_GetScrobbleEnabled()).ToJsonString()));
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
                switch (api.Player_GetRepeat())
                {
                    case RepeatMode.None:
                        api.Player_SetRepeat(RepeatMode.All);
                        break;
                    case RepeatMode.All:
                        api.Player_SetRepeat(RepeatMode.None);
                        break;
                    case RepeatMode.One:
                        api.Player_SetRepeat(RepeatMode.None);
                        break;
                }
            }
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerRepeat,
                        api.Player_GetRepeat()).ToJsonString()));
        }

        public void RequestNowPlayingListPage(string clientId, int offset = 0, int limit = 4000)
        {
            api.NowPlayingList_QueryFiles(null);

            var tracks = new List<NowPlaying>();
            var position = 1;
            while (true)
            {
                var trackPath = api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(trackPath))
                    break;

                var artist = api.Library_GetFileTag(trackPath, MetaDataType.Artist);
                var title = api.Library_GetFileTag(trackPath, MetaDataType.TrackTitle);

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
            api.NowPlayingList_QueryFiles(null);

            var trackList = new List<NowPlayingListTrack>();
            var position = 1;
            while (position <= UserSettings.Instance.NowPlayingListLimit)
            {
                var trackPath = api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(trackPath))
                    break;

                var artist = api.Library_GetFileTag(trackPath, MetaDataType.Artist);
                var title = api.Library_GetFileTag(trackPath, MetaDataType.TrackTitle);

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
            try
            {
                char a = Convert.ToChar(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
                rating = rating.Replace('.', a);
                float fRating;
                if (!float.TryParse(rating, out fRating))
                {
                    fRating = -1;
                }
                if (fRating >= 0 && fRating <= 5)
                {
                    api.Library_SetFileTag(api.NowPlaying_GetFileUrl(), MetaDataType.Rating, fRating.ToString());
                    api.Library_CommitTagsToFile(api.NowPlaying_GetFileUrl());
                    api.Player_GetShowRatingTrack();
                    api.MB_RefreshPanels();
                }
                rating = api.Library_GetFileTag(
                    api.NowPlaying_GetFileUrl(), MetaDataType.Rating).Replace(a, '.');

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
            if (!string.IsNullOrEmpty(api.NowPlaying_GetLyrics()))
            {
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingLyrics,
                            api.NowPlaying_GetLyrics()).ToJsonString()));
            }
            else if (api.ApiRevision >= 17)
            {
                var lyrics = api.NowPlaying_GetDownloadedLyrics();
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingLyrics,
                            !string.IsNullOrEmpty(lyrics) ? lyrics : "Retrieving Lyrics").ToJsonString()));
            }
            else
            {
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingLyrics,
                            "Lyrics Not Found").ToJsonString()));
            }
        }

        /// <summary>
        /// Requests the Now Playing Track Cover. If the cover is available it is dispatched along with an event.
        /// If not, and the ApiRevision is equal or greater than r17 a request for the downloaded artwork is
        /// initiated. The cover is dispatched along with an event when ready.
        /// </summary>
        public void RequestNowPlayingTrackCover()
        {
            if (!String.IsNullOrEmpty(api.NowPlaying_GetArtwork()))
            {
                EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange,
                    api.NowPlaying_GetArtwork()));
            }
            else if (api.ApiRevision >= 17)
            {
                var cover = api.NowPlaying_GetDownloadedArtwork();
                EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange,
                    !String.IsNullOrEmpty(cover) ? cover : String.Empty));
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
                    api.Player_SetPosition(newPosition);
                }
            }
            int currentPosition = api.Player_GetPosition();
            int totalDuration = api.NowPlaying_GetDuration();

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
        /// <returns></returns>
        public void NowPlayingPlay(string index)
        {
            bool result = false;
            int trackIndex;
            if (int.TryParse(index, out trackIndex))
            {
                api.NowPlayingList_QueryFiles(null);
                string trackToPlay = String.Empty;
                int lTrackIndex = 0;
                while (trackIndex != lTrackIndex)
                {
                    trackToPlay = api.NowPlayingList_QueryGetNextFile();
                    lTrackIndex++;
                }
                if (!String.IsNullOrEmpty(trackToPlay))
                    result = api.NowPlayingList_PlayNow(trackToPlay);
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
                success = api.NowPlayingList_RemoveAt(index),
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
                if (!api.Player_GetAutoDjEnabled())
                {
                    api.Player_StartAutoDj();
                }
                else
                {
                    api.Player_EndAutoDj();
                }
            }
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerAutoDj,
                        api.Player_GetAutoDjEnabled()).ToJsonString()));
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
            var hwnd = api.MB_GetWindowHandle();
            var MB = (Form) Form.FromHandle(hwnd);

            if (action.Equals("toggle", StringComparison.OrdinalIgnoreCase))
            {
                if (GetLfmStatus() == LastfmStatus.Love || GetLfmStatus() == LastfmStatus.Ban)
                {
                    MB.Invoke(new MethodInvoker(SetLfmNormalStatus));
                }
                else
                {
                    MB.Invoke(new MethodInvoker(SetLfmLoveStatus));
                }
            }
            else if (action.Equals("love", StringComparison.OrdinalIgnoreCase))
            {
                MB.Invoke(new MethodInvoker(SetLfmLoveStatus));
            }
            else if (action.Equals("ban", StringComparison.OrdinalIgnoreCase))
            {
                MB.Invoke(new MethodInvoker(SetLfmLoveBan));
            }

            var data = new SocketMessage(Constants.NowPlayingLfmRating, GetLfmStatus()).ToJsonString();
            EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, data));
        }

        private void SetLfmNormalStatus()
        {
            api.Library_SetFileTag(
                api.NowPlaying_GetFileUrl(), MetaDataType.RatingLove, "lfm");
        }

        private void SetLfmLoveStatus()
        {
            api.Library_SetFileTag(
                api.NowPlaying_GetFileUrl(), MetaDataType.RatingLove, "Llfm");
        }

        private void SetLfmLoveBan()
        {
            api.Library_SetFileTag(
                api.NowPlaying_GetFileUrl(), MetaDataType.RatingLove, "Blfm");
        }

        private LastfmStatus GetLfmStatus()
        {
            LastfmStatus lastfmStatus;
            string apiReply = api.NowPlaying_GetFileTag(MetaDataType.RatingLove);
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
            api.Playlist_QueryPlaylists();
            var playlists = new List<Playlist>();
            while (true)
            {
                var url = api.Playlist_QueryGetNextPlaylist();

                if (string.IsNullOrEmpty(url))
                {
                    break;
                }

                var name = api.Playlist_GetName(url);

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
            var success = api.Playlist_PlayNow(url);
            var data = new SocketMessage(Constants.PlaylistPlay, success).ToJsonString();
            EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, data, clientId));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="clientId"></param>
        public void RequestPlayerStatus(string clientId)
        {
            var status = new Dictionary<string, object>
            {
                [Constants.PlayerRepeat] = api.Player_GetRepeat().ToString(),
                [Constants.PlayerMute] = api.Player_GetMute(),
                [Constants.PlayerShuffle] = Authenticator.ClientProtocolMisMatch(clientId)
                    ? (object) api.Player_GetShuffle()
                    : GetShuffleState(),
                [Constants.PlayerScrobble] = api.Player_GetScrobbleEnabled(),
                [Constants.PlayerState] = api.Player_GetPlayState().ToString(),
                [Constants.PlayerVolume] = ((int) Math.Round(api.Player_GetVolume()*100, 1)).ToString(
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
            EventBus.FireEvent(new MessageEvent(
                EventType.ReplyAvailable,
                new SocketMessage(Constants.NowPlayingTrack, GetTrackInfo()).ToJsonString(), clientId));
        }


        /// <summary>
        /// Moves a track of the now playing list to a new position.
        /// </summary>
        /// <param name="clientId">The Id of the client that initiated the request</param>
        /// <param name="from">The initial position</param>
        /// <param name="to">The final position</param>
        public void RequestNowPlayingMove(string clientId, int from, int to)
        {
            bool result = false;
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
            result = api.NowPlayingList_MoveFiles(aFrom, dIn);

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

            if (api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", filter))
            {
                try
                {
                    foreach (
                        var entry in
                        new List<string>(api.Library_QueryGetLookupTableValue(null)
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

            api.Library_QueryLookupTable(null, null, null);

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
            if (api.Library_QueryFiles(XmlFilter(new[] {"ArtistPeople"}, artist, true)))
            {
                while (true)
                {
                    var currentFile = api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentFile)) break;
                    var current = new Album(api.Library_GetFileTag(currentFile, MetaDataType.AlbumArtist),
                        api.Library_GetFileTag(currentFile, MetaDataType.Album));
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

            if (api.Library_QueryLookupTable("artist", "count",
                XmlFilter(new[] {"ArtistPeople"}, artist, false)))
            {
                artistList.AddRange(api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split('\0'))
                    .Select(artistInfo => new Artist(artistInfo[0], int.Parse(artistInfo[1]))));
            }

            api.Library_QueryLookupTable(null, null, null);

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

            if (api.Library_QueryLookupTable("artist", "count", XmlFilter(new[] {"Genre"}, genre, true)))
            {
                artistList.AddRange(api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split('\0'))
                    .Select(artistInfo => new Artist(artistInfo[0], int.Parse(artistInfo[1]))));
            }

            api.Library_QueryLookupTable(null, null, null);
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibraryGenreArtists,
                        artistList).ToJsonString(), clientId));

            artistList = null;
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
            if (api.Library_QueryLookupTable("genre", "count", query))
            {
                genreList.AddRange(api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split(new[] {'\0'}, StringSplitOptions.None))
                    .Select(genreInfo => new Genre(genreInfo[0], int.Parse(genreInfo[1]))));
            }
            api.Library_QueryLookupTable(null, null, null);

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchGenre,
                        genreList).ToJsonString(), clientId));

            genreList = null;
        }

        public void LibraryBrowseGenres(string clientId, int offset = 0, int limit = 4000)
        {
            var genres = new List<Genre>();
            if (api.Library_QueryLookupTable("genre", "count", null))
            {
                genres.AddRange(api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split(new[] {'\0'}, StringSplitOptions.None))
                    .Select(genreInfo => new Genre(genreInfo[0], int.Parse(genreInfo[1]))));
            }
            api.Library_QueryLookupTable(null, null, null);

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

        public void LibraryBrowseArtists(string clientId, int offset = 0, int limit = 4000)
        {
            var artists = new List<Artist>();

            if (api.Library_QueryLookupTable("artist", "count", null))
            {
                artists.AddRange(api.Library_QueryGetLookupTableValue(null)
                    .Split(new[] {"\0\0"}, StringSplitOptions.None)
                    .Select(entry => entry.Split('\0'))
                    .Select(artistInfo => new Artist(artistInfo[0], int.Parse(artistInfo[1]))));
            }

            api.Library_QueryLookupTable(null, null, null);
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

        public void LibraryBrowseAlbums(string clientId, int offset = 0, int limit = 4000)
        {
            var albums = new List<Album>();

            if (api.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", null))
            {
                try
                {
                    var data = new List<string>(api.Library_QueryGetLookupTableValue(null)
                        .Split(new[] {"\0\0"}, StringSplitOptions.None));
                    foreach (var entry in data)
                    {
                        if (string.IsNullOrEmpty(entry)) continue;

                        var albumInfo = entry.Split('\0');
                        if (albumInfo.Length < 2) continue;

                        var current = albumInfo.Length == 3
                            ? new Album(albumInfo[1], albumInfo[2])
                            : new Album(albumInfo[0], albumInfo[1]);


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

            api.Library_QueryLookupTable(null, null, null);

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
            if (api.Library_QueryFiles(null))
            {
                while (true)
                {
                    var currentTrack = api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack)) break;

                    int trackNumber;
                    int discNumber;

                    int.TryParse(api.Library_GetFileTag(currentTrack, MetaDataType.TrackNo), out trackNumber);
                    int.TryParse(api.Library_GetFileTag(currentTrack, MetaDataType.DiscNo), out discNumber);

                    var track = new Track
                    {
                        Artist = api.Library_GetFileTag(currentTrack, MetaDataType.Artist),
                        Title = api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle),
                        Album = api.Library_GetFileTag(currentTrack, MetaDataType.Album),
                        AlbumArtist = api.Library_GetFileTag(currentTrack, MetaDataType.AlbumArtist),
                        Genre = api.Library_GetFileTag(currentTrack, MetaDataType.Genre),
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="title"></param>
        /// <param name="clientId"></param>
        public void LibrarySearchTitle(string title, string clientId)
        {
            var tracks = new List<Track>();
            if (api.Library_QueryFiles(XmlFilter(new[] {"Title"}, title, false)))
            {
                while (true)
                {
                    var currentTrack = api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack)) break;

                    var trackNumber = 0;
                    int.TryParse(api.Library_GetFileTag(currentTrack, MetaDataType.TrackNo), out trackNumber);
                    var src = currentTrack;

                    tracks.Add(new Track(api.Library_GetFileTag(currentTrack, MetaDataType.Artist),
                        api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle),
                        trackNumber, src));
                }
            }

            api.Library_QueryLookupTable(null, null, null);
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchTitle, tracks).ToJsonString(), clientId));

            tracks = null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="album"></param>
        /// <param name="client"></param>
        public void LibraryGetAlbumTracks(string album, string client)
        {
            var trackList = new List<Track>();
            if (api.Library_QueryFiles(XmlFilter(new[] {"Album"}, album, true)))
            {
                while (true)
                {
                    var currentTrack = api.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack)) break;

                    int trackNumber;
                    int.TryParse(api.Library_GetFileTag(currentTrack, MetaDataType.TrackNo), out trackNumber);
                    var src = currentTrack;

                    trackList.Add(new Track(api.Library_GetFileTag(currentTrack, MetaDataType.Artist),
                        api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle), trackNumber, src));
                }
                trackList.Sort();
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibraryAlbumTracks, trackList).ToJsonString(), client));
        }

        public void RequestRadioStations(string clientId)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queue"></param>
        /// <param name="tag"></param>
        /// <param name="query"></param>
        public void RequestQueueFiles(QueueType queue, MetaTag tag, string query)
        {
            var trackList = tag != MetaTag.title ? GetUrlsForTag(tag, query) : new[] {query};

            switch (queue)
            {
                case QueueType.Next:
                    api.NowPlayingList_QueueFilesNext(trackList);
                    break;
                case QueueType.Last:
                    api.NowPlayingList_QueueFilesLast(trackList);
                    break;
                case QueueType.PlayNow:
                    api.NowPlayingList_Clear();
                    api.NowPlayingList_QueueFilesLast(trackList);
                    api.NowPlayingList_PlayNow(trackList[0]);
                    break;
            }
        }

        /// <summary>
        /// Takes a given query string and searches the Now Playing list for any track with a matching title or artist.
        /// The title is checked first.
        /// </summary>
        /// <param name="query">The string representing the query</param>
        /// <param name="clientId">Client</param>
        public void NowPlayingSearch(string query, string clientId)
        {
            bool result = false;
            api.NowPlayingList_QueryFiles(XmlFilter(new[] {"ArtistPeople", "Title"}, query, false));

            while (true)
            {
                string currentTrack = api.NowPlayingList_QueryGetNextFile();
                if (String.IsNullOrEmpty(currentTrack)) break;
                string artist = api.Library_GetFileTag(currentTrack, MetaDataType.Artist);
                string title = api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle);

                if (title.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                result = api.NowPlayingList_PlayNow(currentTrack);
                break;
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListSearch, result).ToJsonString(), clientId));
        }

        public string[] GetUrlsForTag(MetaTag tag, string query)
        {
            var filter = String.Empty;
            string[] tracks = {};
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
            }

            api.Library_QueryFilesEx(filter, ref tracks);

            var list = tracks.Select(file => new MetaData
            {
                file = file,
                artist = api.Library_GetFileTag(file, MetaDataType.Artist),
                album_artist = api.Library_GetFileTag(file, MetaDataType.AlbumArtist),
                album = api.Library_GetFileTag(file, MetaDataType.Album),
                title = api.Library_GetFileTag(file, MetaDataType.TrackTitle),
                genre = api.Library_GetFileTag(file, MetaDataType.Genre),
                year = api.Library_GetFileTag(file, MetaDataType.Year),
                track_no = api.Library_GetFileTag(file, MetaDataType.TrackNo),
                disc = api.Library_GetFileTag(file, MetaDataType.DiscNo)
            }).ToList();
            list.Sort();
            tracks = list.Select(r => r.file)
                .ToArray();

            return tracks;
        }

        public void RequestPlay(string clientId)
        {
            var state = api.Player_GetPlayState();

            if (state != PlayState.Playing)
            {
                api.Player_PlayPause();
            }
        }

        public void RequestPausePlayback(string clientId)
        {
            var state = api.Player_GetPlayState();

            if (state == PlayState.Playing)
            {
                api.Player_PlayPause();
            }
        }

        /// <summary>
        ///     Initializes the logging configuration.
        /// </summary>
        /// <param name="storagePath"></param>
        public static void InitializeLoggingConfiguration(string storagePath, LogLevel logLevel)
        {
            var config = new LoggingConfiguration();

            var consoleTarget = new ColoredConsoleTarget();
            var fileTarget = new FileTarget()
            {
                ArchiveAboveSize = 2097152,
                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                EnableArchiveFileCompression = true
            };
            var debugger = new DebuggerTarget();


#if DEBUG
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
#endif

            config.AddTarget("console", consoleTarget);
            config.AddTarget("file", fileTarget);
            config.AddTarget("debugger", debugger);

            consoleTarget.Layout = @"${date:format=HH\\:MM\\:ss} ${logger} ${message} ${exception}";
            fileTarget.FileName = $"{storagePath}\\error.log";
            fileTarget.Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}||${exception}";

            debugger.Layout = fileTarget.Layout;

            var consoleRule = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(consoleRule);

            var fileRule = new LoggingRule("*", logLevel, fileTarget);

            config.LoggingRules.Add(fileRule);

            var debuggerRule = new LoggingRule("*", LogLevel.Debug, debugger);
            config.LoggingRules.Add(debuggerRule);

            LogManager.Configuration = config;
        }
    }
}