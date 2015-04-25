using System.Windows.Forms;

namespace MusicBeePlugin
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using AndroidRemote;
    using AndroidRemote.Events;
    using AndroidRemote.Networking;
    using ServiceStack.Text;
    using System;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Timers;
    using AndroidRemote.Controller;
    using AndroidRemote.Entities;
    using AndroidRemote.Error;
    using AndroidRemote.Settings;
    using AndroidRemote.Utilities;
    using AndroidRemote.Enumerations;
    using Timer = System.Timers.Timer;

    /// <summary>
    /// The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
    public partial class Plugin
    {
        /// <summary>
        /// The mb api interface.
        /// </summary>
        private MusicBeeApiInterface mbApiInterface;

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

            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);

            UserSettings.Instance.SetStoragePath(mbApiInterface.Setting_GetPersistentStoragePath());
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

            if (mbApiInterface.ApiRevision < MinApiRevision)
            {
                return about;
            }

            ErrorHandler.SetLogFilePath(mbApiInterface.Setting_GetPersistentStoragePath());

            StartPlayerStatusMonitoring();

            mbApiInterface.MB_AddMenuItem("mnuTools/MusicBee Remote", "Information Panel of the MusicBee Remote",
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
            if (mbApiInterface.Player_GetPlayState() == PlayState.Playing)
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
            scrobble = mbApiInterface.Player_GetScrobbleEnabled();
            repeat = mbApiInterface.Player_GetRepeat();
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
            if (GetShuffleState() != _shuffleState)
            {
                _shuffleState = GetShuffleState();
                EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, new SocketMessage(
                                                                                  Constants.PlayerShuffle, _shuffleState)
                                                                                  .ToJsonString()));
            }

            if (mbApiInterface.Player_GetScrobbleEnabled() != scrobble)
            {
                scrobble = mbApiInterface.Player_GetScrobbleEnabled();
                EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                                                    new SocketMessage(Constants.PlayerScrobble, scrobble)
                                                        .ToJsonString()));
            }

            if (mbApiInterface.Player_GetRepeat() != repeat)
            {
                repeat = mbApiInterface.Player_GetRepeat();
                EventBus.FireEvent(new MessageEvent(
                                       EventType.ReplyAvailable,
                                       new SocketMessage(Constants.PlayerRepeat, repeat).ToJsonString()));
            }
        }

        public void OpenInfoWindow()
        {
            IntPtr hwnd = mbApiInterface.MB_GetWindowHandle();
            Form MB = (Form)Form.FromHandle(hwnd);
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
            string settingsFolder = mbApiInterface.Setting_GetPersistentStoragePath + "\\mb_remote";
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
                                                                               mbApiInterface.Player_GetVolume()*100,
                                                                               1))).ToJsonString()));
                    break;
                case NotificationType.VolumeMuteChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                                                        new SocketMessage(Constants.PlayerMute,
                                                                          mbApiInterface.Player_GetMute()).ToJsonString()
                                           ));
                    break;
                case NotificationType.PlayStateChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, new SocketMessage(Constants.PlayerState,
                                                                                                    mbApiInterface
                                                                                                        .Player_GetPlayState
                                                                                                        ()).ToJsonString
                                                                                      ()));
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    if (mbApiInterface.ApiRevision >= 17)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.NowPlayingLyricsChange,
                            !String.IsNullOrEmpty(mbApiInterface.NowPlaying_GetDownloadedLyrics())
                                ? mbApiInterface.NowPlaying_GetDownloadedLyrics() : "Lyrics Not Found" ));
                    }
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    if (mbApiInterface.ApiRevision >= 17)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange,
                                                            mbApiInterface.NowPlaying_GetDownloadedArtwork()));
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
                    Artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist),
                    Album = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album),
                    Year = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Year)
                };
            nowPlayingTrack.SetTitle(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle),
                                     mbApiInterface.NowPlaying_GetFileUrl());
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
                        mbApiInterface.Player_PlayNextTrack()).ToJsonString()));
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
                        mbApiInterface.Player_Stop()).ToJsonString()));
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
                        mbApiInterface.Player_PlayPause()).ToJsonString()));
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
                        mbApiInterface.Player_PlayPreviousTrack()).ToJsonString()));
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
                mbApiInterface.Player_SetVolume((float) volume/100);
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerVolume,
                        ((int)Math.Round(mbApiInterface.Player_GetVolume() * 100, 1))).ToJsonString()));

            if (mbApiInterface.Player_GetMute())
            {
                mbApiInterface.Player_SetMute(false);
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
                mbApiInterface.Player_SetShuffle(!mbApiInterface.Player_GetShuffle());
            }
            
            EventBus.FireEvent(
                new MessageEvent(
                    EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerShuffle,
                        mbApiInterface.Player_GetShuffle()).ToJsonString()));
        }

        /// <summary>
        /// Changes the player shuffle and autodj state following the model of MusicBee. 
        /// </summary>
        /// <param name="action"></param>
        public void RequestAutoDjShuffleState(StateAction action)
        {

            var shuffleEnabled = mbApiInterface.Player_GetShuffle();
            var autoDjEnabled = mbApiInterface.Player_GetAutoDjEnabled();
    
            if (action == StateAction.Toggle)
            {
                if (shuffleEnabled && !autoDjEnabled)
                {
                    mbApiInterface.Player_StartAutoDj();

                }
                else if (autoDjEnabled)
                {
                    mbApiInterface.Player_EndAutoDj();
                }
                else
                {
                    mbApiInterface.Player_SetShuffle(true);
                }
            }
            
            var socketMessage = new SocketMessage(Constants.PlayerShuffle, GetShuffleState());
            var messageEvent = new MessageEvent(EventType.ReplyAvailable, socketMessage.ToJsonString());
            EventBus.FireEvent(messageEvent);
        }

        private ShuffleState GetShuffleState()
        {
            var shuffleEnabled = mbApiInterface.Player_GetShuffle();
            var autoDjEnabled = mbApiInterface.Player_GetAutoDjEnabled();
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
                mbApiInterface.Player_SetMute(!mbApiInterface.Player_GetMute());
            }
            
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerMute,
                        mbApiInterface.Player_GetMute()).ToJsonString()));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        public void RequestScrobblerState(StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                mbApiInterface.Player_SetScrobbleEnabled(!mbApiInterface.Player_GetScrobbleEnabled());
            }
            
            EventBus.FireEvent(
                new MessageEvent(
                    EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerScrobble,
                        mbApiInterface.Player_GetScrobbleEnabled()).ToJsonString()));
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
                switch (mbApiInterface.Player_GetRepeat())
                {
                    case RepeatMode.None:
                        mbApiInterface.Player_SetRepeat(RepeatMode.All);
                        break;
                    case RepeatMode.All:
                        mbApiInterface.Player_SetRepeat(RepeatMode.None);
                        break;
                    case RepeatMode.One:
                        mbApiInterface.Player_SetRepeat(RepeatMode.None);
                        break;
                }
            }
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerRepeat,
                        mbApiInterface.Player_GetRepeat()).ToJsonString()));
        }

        /// <summary>
        /// It gets the 100 first tracks of the playlist and returns them in an XML formated String without a root element.
        /// </summary>
        /// <param name="clientProtocolVersion"> </param>
        /// <param name="clientId"> </param>
        /// <returns>XML formated string without root element</returns>
        public void RequestNowPlayingList(double clientProtocolVersion, string clientId)
        {
            mbApiInterface.NowPlayingList_QueryFiles(null);

            List<NowPlayingListTrack> trackList = new List<NowPlayingListTrack>();
            int position = 1;
            while (position <= UserSettings.Instance.NowPlayingListLimit)
            {
                string playListTrack = mbApiInterface.NowPlayingList_QueryGetNextFile();
                if (String.IsNullOrEmpty(playListTrack))
                    break;

                string artist = mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.Artist);
                string title = mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.TrackTitle);

                if (String.IsNullOrEmpty(artist))
                {
                    artist = "Unknown Artist";
                }

                if (String.IsNullOrEmpty(title))
                {
                    int index = playListTrack.LastIndexOf('\\');
                    title = playListTrack.Substring(index + 1);
                }

                trackList.Add(
                    new NowPlayingListTrack(artist, title, position));
                position++;
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingList,
                        trackList).ToJsonString(),clientId));
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
                if (fRating >= 0  && fRating <= 5)
                {
                    mbApiInterface.Library_SetFileTag(mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating, fRating.ToString());
                    mbApiInterface.Library_CommitTagsToFile(mbApiInterface.NowPlaying_GetFileUrl());
                    mbApiInterface.Player_GetShowRatingTrack();
                    mbApiInterface.MB_RefreshPanels();
                }
                rating = mbApiInterface.Library_GetFileTag(
                    mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating).Replace(a, '.');
                
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingRating,
                            rating).ToJsonString()));
            }
            catch (Exception e)
            {
#if DEBUG
                ErrorHandler.LogError(e);
#endif
            }
        }

        /// <summary>
        /// Requests the Now Playing track lyrics. If the lyrics are available then they are dispatched along with
        /// and event. If not, and the ApiRevision is equal or greater than r17 a request for the downloaded lyrics
        /// is initiated. The lyrics are dispatched along with and event when ready.
        /// </summary>
        public void RequestNowPlayingTrackLyrics()
        {
            if (!String.IsNullOrEmpty(mbApiInterface.NowPlaying_GetLyrics()))
            {
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingLyrics,
                            mbApiInterface.NowPlaying_GetLyrics()).ToJsonString()));
            }
            else if (mbApiInterface.ApiRevision >= 17)
            {
                string lyrics = mbApiInterface.NowPlaying_GetDownloadedLyrics();
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingLyrics,
                            !String.IsNullOrEmpty(lyrics) ? lyrics : "Retrieving Lyrics").ToJsonString()));
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
            if (!String.IsNullOrEmpty(mbApiInterface.NowPlaying_GetArtwork()))
            {
                EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange,
                                                    mbApiInterface.NowPlaying_GetArtwork()));
            }
            else if (mbApiInterface.ApiRevision >= 17)
            {
                var cover = mbApiInterface.NowPlaying_GetDownloadedArtwork();
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
                    mbApiInterface.Player_SetPosition(newPosition);
                }
            }
            int currentPosition = mbApiInterface.Player_GetPosition();
            int totalDuration = mbApiInterface.NowPlaying_GetDuration();

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
                mbApiInterface.NowPlayingList_QueryFiles(null);
                string trackToPlay = String.Empty;
                int lTrackIndex = 0;
                while (trackIndex != lTrackIndex)
                {
                    trackToPlay = mbApiInterface.NowPlayingList_QueryGetNextFile();
                    lTrackIndex++;
                }
                if (!String.IsNullOrEmpty(trackToPlay))
                    result = mbApiInterface.NowPlayingList_PlayNow(trackToPlay);
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
                success = mbApiInterface.NowPlayingList_RemoveAt(index),
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
                if (!mbApiInterface.Player_GetAutoDjEnabled())
                {
                    mbApiInterface.Player_StartAutoDj();
                }
                else
                {
                    mbApiInterface.Player_EndAutoDj();
                }
            }
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerAutoDj,
                        mbApiInterface.Player_GetAutoDjEnabled()).ToJsonString()));
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
            var hwnd = mbApiInterface.MB_GetWindowHandle();
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
            mbApiInterface.Library_SetFileTag(
                    mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.RatingLove, "lfm");
        }

        private void SetLfmLoveStatus()
        {
            mbApiInterface.Library_SetFileTag(
                    mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.RatingLove, "Llfm");
        }

        private void SetLfmLoveBan()
        {
            mbApiInterface.Library_SetFileTag(
                    mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.RatingLove, "Blfm");
        }

        private LastfmStatus GetLfmStatus()
        {
            LastfmStatus lastfmStatus;
            string apiReply = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.RatingLove);
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
        public void GetAvailablePlaylistUrls()
        {
            mbApiInterface.Playlist_QueryPlaylists();
            string playlistUrl;
            while (true)
            {
                playlistUrl = mbApiInterface.Playlist_QueryGetNextPlaylist();
                if (string.IsNullOrEmpty(playlistUrl)) break;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientId"></param>
        public void RequestPlayerStatus(string clientId)
        {
            var status = new Dictionary<string, object>
            {
                [Constants.PlayerRepeat] = mbApiInterface.Player_GetRepeat().ToString(),
                [Constants.PlayerMute] = mbApiInterface.Player_GetMute(),
                [Constants.PlayerShuffle] = Authenticator.ClientProtocolMisMatch(clientId)
                    ? (object) mbApiInterface.Player_GetShuffle()
                    : GetShuffleState(),
                [Constants.PlayerScrobble] = mbApiInterface.Player_GetScrobbleEnabled(),
                [Constants.PlayerState] = mbApiInterface.Player_GetPlayState().ToString(),
                [Constants.PlayerVolume] = ((int) Math.Round(mbApiInterface.Player_GetVolume()*100, 1)).ToString(
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
            result = mbApiInterface.NowPlayingList_MoveFiles(aFrom, dIn);

            var reply = new
            {
                success = result, from, to
            };
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListMove,reply).ToJsonString(), clientId));
        }

        private string XmlFilter(string[] tags, string query, bool isStrict)
        {
            XElement filter = new XElement("Source",
                                           new XAttribute("Type", 1));

            XElement conditions = new XElement("Conditions",
                                               new XAttribute("CombineMethod", "Any"));
            foreach (string tag in tags)
            {
                XElement condition = new XElement("Condition",
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
            List<Album> albumList = new List<Album>();

            if (mbApiInterface.Library_QueryLookupTable("album", "albumartist" + '\0' + "album", XmlFilter(new[] {"Album"}, albumName, false)))
            {
                try
                {
                    foreach (string entry in new List<string>(mbApiInterface.Library_QueryGetLookupTableValue(null).Split(new[] {"\0\0"}, StringSplitOptions.None)))
                    {
                        if (String.IsNullOrEmpty(entry)) continue;
                        string[] albumInfo = entry.Split('\0');
                        if (albumInfo.Length < 2) continue;

                        Album current = albumInfo.Length == 3
                                            ? new Album(albumInfo[1], albumInfo[2])
                                            : new Album(albumInfo[0], albumInfo[1]);
                        if (current.album.IndexOf(albumName, StringComparison.OrdinalIgnoreCase) < 0) continue;

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
                catch (IndexOutOfRangeException)
                {
                }
            }

            mbApiInterface.Library_QueryLookupTable(null, null, null);
            
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchAlbum,
                        albumList).ToJsonString(), clientId));
            albumList = null;
        }
        
        /// <summary>
        /// Used to get all the albums by the specified artist.
        /// </summary>
        /// <param name="artist"></param>
        /// <param name="clientId"></param>
        public void LibraryGetArtistAlbums(string artist, string clientId)
        {
            List<Album> albumList = new List<Album>();
            if (mbApiInterface.Library_QueryFiles(XmlFilter(new[] {"ArtistPeople"}, artist, true)))
            {
                while (true)
                {
                    string currentFile = mbApiInterface.Library_QueryGetNextFile();
                    if (String.IsNullOrEmpty(currentFile)) break;
                    Album current = new Album(mbApiInterface.Library_GetFileTag(currentFile, MetaDataType.AlbumArtist),
                                              mbApiInterface.Library_GetFileTag(currentFile, MetaDataType.Album));
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
            albumList = null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="artist"></param>
        /// <param name="clientId"></param>
        public void LibrarySearchArtist(string artist, string clientId)
        {
            List<Artist> artistList = new List<Artist>();
            if (mbApiInterface.Library_QueryLookupTable("artist", "count",
                                                        XmlFilter(new[] {"ArtistPeople"}, artist, false)))
            {
                foreach (string entry in mbApiInterface.Library_QueryGetLookupTableValue(null).Split(new[] {"\0\0"}, StringSplitOptions.None))
                {
                    string[] artistInfo = entry.Split(new[] { '\0' });
                    artistList.Add(new Artist(artistInfo[0], int.Parse(artistInfo[1])));
                }
            }

            mbApiInterface.Library_QueryLookupTable(null, null, null);

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchArtist,
                        artistList).ToJsonString(),clientId));
            artistList = null;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="genre"></param>
        /// <param name="clientId"></param>
        public void LibraryGetGenreArtists(string genre, string clientId)
        {
            List<Artist> artistList = new List<Artist>();

            if (mbApiInterface.Library_QueryLookupTable("artist", "count", XmlFilter(new[] {"Genre"}, genre, true)))
            {
                foreach (string entry in mbApiInterface.Library_QueryGetLookupTableValue(null).Split(new[] {"\0\0"}, StringSplitOptions.None))
                {
                    string[] artistInfo = entry.Split(new[] {'\0'});
                    artistList.Add(new Artist(artistInfo[0], int.Parse(artistInfo[1])));        
                }
            }

            mbApiInterface.Library_QueryLookupTable(null, null, null);
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
            List<Genre> genreList = new List<Genre>();
            if (mbApiInterface.Library_QueryLookupTable("genre", "count",
                                                        XmlFilter(new[] {"Genre"}, genre, false)))
            {
                foreach (string entry in mbApiInterface.Library_QueryGetLookupTableValue(null).Split(new[] {"\0\0"}, StringSplitOptions.None))
                {
                    string[] genreInfo = entry.Split(new[] {'\0'}, StringSplitOptions.None);
                    genreList.Add(new Genre(genreInfo[0], int.Parse(genreInfo[1])));   
                }
            }
            mbApiInterface.Library_QueryLookupTable(null, null, null);

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchGenre,
                        genreList).ToJsonString(), clientId));

            genreList = null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="title"></param>
        /// <param name="clientId"></param>
        public void LibrarySearchTitle(string title, string clientId)
        {
            var tracks = new List<Track>();
            if (mbApiInterface.Library_QueryFiles(XmlFilter(new[] {"Title"}, title, false)))
            {
                while (true)
                {
                    var currentTrack = mbApiInterface.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack)) break;

                    var trackNumber = 0;
                    int.TryParse(mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.TrackNo), out trackNumber);
                    var src = currentTrack;

                    tracks.Add(new Track(mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.Artist),
                                         mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle),
                                         trackNumber, src));
                }
            }

            mbApiInterface.Library_QueryLookupTable(null, null, null);
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchTitle, tracks).ToJsonString(),clientId));

            tracks = null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="album"></param>
        /// <param name="client"></param>
        public void LibraryGetAlbumTracks(string album, string client)
        {
            List<Track> trackList = new List<Track>();
            if (mbApiInterface.Library_QueryFiles(XmlFilter(new[] {"Album"}, album, true)))
            { 
                while (true)
                {
                    string currentTrack = mbApiInterface.Library_QueryGetNextFile();
                    if (string.IsNullOrEmpty(currentTrack)) break;

                    int trackNumber = 0;
                    int.TryParse(mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.TrackNo), out trackNumber);
                    string src = currentTrack;

                    trackList.Add(new Track(mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.Artist),
                                              mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle), trackNumber, src));
                }
                trackList.Sort();
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibraryAlbumTracks, trackList).ToJsonString(), client));

            trackList = null;
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
                    mbApiInterface.NowPlayingList_QueueFilesNext(trackList);
                    break;
                case QueueType.Last:
                    mbApiInterface.NowPlayingList_QueueFilesLast(trackList);
                    break;
                case QueueType.PlayNow:
                    mbApiInterface.NowPlayingList_Clear();
                    mbApiInterface.NowPlayingList_QueueFilesLast(trackList);
                    mbApiInterface.NowPlayingList_PlayNow(trackList[0]);
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
            mbApiInterface.NowPlayingList_QueryFiles(XmlFilter(new[] {"ArtistPeople", "Title"}, query, false));

            while (true)
            {
                string currentTrack = mbApiInterface.NowPlayingList_QueryGetNextFile();
                if (String.IsNullOrEmpty(currentTrack)) break;
                string artist = mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.Artist);
                string title = mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle);

                if (title.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                result = mbApiInterface.NowPlayingList_PlayNow(currentTrack);
                break;
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListSearch,result).ToJsonString(), clientId));
        }

        public string[] GetUrlsForTag(MetaTag tag, string query)
        {
            var filter = String.Empty;
            string[] tracks = { };
            switch (tag)
            {
                case MetaTag.artist:
                    filter = XmlFilter(new[] { "ArtistPeople" }, query, true);
                    break;
                case MetaTag.album:
                    filter = XmlFilter(new[] { "Album" }, query, true);
                    break;
                case MetaTag.genre:
                    filter = XmlFilter(new[] { "Genre" }, query, true);
                    break;
            }

            mbApiInterface.Library_QueryFilesEx(filter, ref tracks);

            var list = tracks.Select(file => new MetaData
            {
                file = file,
                artist = mbApiInterface.Library_GetFileTag(file, MetaDataType.Artist),
                album_artist = mbApiInterface.Library_GetFileTag(file, MetaDataType.AlbumArtist),
                album = mbApiInterface.Library_GetFileTag(file, MetaDataType.Album),
                title = mbApiInterface.Library_GetFileTag(file, MetaDataType.TrackTitle),
                genre = mbApiInterface.Library_GetFileTag(file, MetaDataType.Genre),
                year = mbApiInterface.Library_GetFileTag(file, MetaDataType.Year),
                track_no = mbApiInterface.Library_GetFileTag(file, MetaDataType.TrackNo),
                disc = mbApiInterface.Library_GetFileTag(file, MetaDataType.DiscNo)
            }).ToList();
            list.Sort();
            tracks = list.Select(r => r.file)
                    .ToArray();

            return tracks;
        }

        public void RequestPlay(string clientId)
        {
            var state = mbApiInterface.Player_GetPlayState();

            if (state != PlayState.Playing)
            {
                mbApiInterface.Player_PlayPause();
            }
        }

        public void RequestPausePlayback(string clientId)
        {
            var state = mbApiInterface.Player_GetPlayState();

            if (state == PlayState.Playing)
            {
                mbApiInterface.Player_PlayPause();
            }
        }
    }
}