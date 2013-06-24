using System.Diagnostics;

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
    using System.Security;
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

        /// <summary>
        /// The shuffle.
        /// </summary>
        private bool shuffle;

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
            about.Name = "MusicBee Remote:Server";
            about.Description = "Remote Control for server to be used with android application.";
            about.Author = "Konstantinos Paparas (aka Kelsos)";
            about.TargetApplication = "MusicBee Remote";

            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            UserSettings.Instance.CurrentVersion = v.ToString();

            // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            about.Type = PluginType.General;
            about.VersionMajor = Convert.ToInt16(v.Major);
            about.VersionMinor = Convert.ToInt16(v.Minor);
            about.Revision = Convert.ToInt16(v.Revision);
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

            return about;
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
        private static void MenuItemClicked(object sender, EventArgs args)
        {
            InfoWindow window = new InfoWindow();
            window.Show();
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
            shuffle = mbApiInterface.Player_GetShuffle();
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
            if (mbApiInterface.Player_GetShuffle() != shuffle)
            {
                shuffle = mbApiInterface.Player_GetShuffle();
                EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, new SocketMessage(
                                                                                  Constants.PlayerShuffle,
                                                                                  Constants.Message, shuffle)
                                                                                  .toJsonString()));
            }

            if (mbApiInterface.Player_GetScrobbleEnabled() != scrobble)
            {
                scrobble = mbApiInterface.Player_GetScrobbleEnabled();
                EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                                                    new SocketMessage(Constants.PlayerScrobble, Constants.Message, scrobble)
                                                        .toJsonString()));
            }

            if (mbApiInterface.Player_GetRepeat() != repeat)
            {
                repeat = mbApiInterface.Player_GetRepeat();
                EventBus.FireEvent(new MessageEvent(
                                       EventType.ReplyAvailable,
                                       new SocketMessage(Constants.PlayerRepeat,
                                                         Constants.Message, repeat).toJsonString()));
            }
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
            return false;
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
                    RequestNowPlayingTrackLyrics();
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                                                        new SocketMessage(Constants.NowPlayingTrack,
                                                                          Constants.Message, GetTrackInfo())
                                                            .toJsonString()));
                    break;
                case NotificationType.VolumeLevelChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                                                        new SocketMessage(Constants.PlayerVolume, Constants.Message,
                                                                          ((int)
                                                                           Math.Round(
                                                                               mbApiInterface.Player_GetVolume()*100,
                                                                               1))).toJsonString()));
                    break;
                case NotificationType.VolumeMuteChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                                                        new SocketMessage(Constants.PlayerMute, Constants.Message,
                                                                          mbApiInterface.Player_GetMute()).toJsonString()
                                           ));
                    break;
                case NotificationType.PlayStateChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable, new SocketMessage(Constants.PlayerState,
                                                                                                    Constants.Message,
                                                                                                    mbApiInterface
                                                                                                        .Player_GetPlayState
                                                                                                        ()).toJsonString
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
                                                            mbApiInterface.NowPlaying_GetDownloadedArtwork(), "",
                                                            mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)));
                    }
                    break;
                case NotificationType.NowPlayingListChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.ReplyAvailable,
                                                        new SocketMessage(Constants.NowPlayingListChanged,
                                                                          Constants.Message, true).toJsonString()));
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
                    new SocketMessage(Constants.PlayerNext, Constants.Reply,
                        mbApiInterface.Player_PlayNextTrack()).toJsonString()));
        }

        /// <summary>
        /// When called stops the playback.
        /// </summary>
        /// <returns></returns>
        public void RequestStopPlayback(string clientId)
        {
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerStop, Constants.Reply,
                        mbApiInterface.Player_Stop()).toJsonString()));
        }

        /// <summary>
        /// When called changes the play/pause state or starts playing a track if the status is stopped.
        /// </summary>
        /// <returns></returns>
        public void RequestPlayPauseTrack(string clientId)
        {
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerPlayPause, Constants.Reply,
                        mbApiInterface.Player_PlayPause()).toJsonString()));
        }

        /// <summary>
        /// When called plays the previous track.
        /// </summary>
        /// <returns></returns>
        public void RequestPreviousTrack(string clientId)
        {
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerPrevious, Constants.Reply,
                        mbApiInterface.Player_PlayPreviousTrack()).toJsonString()));
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
                    new SocketMessage(Constants.PlayerVolume, Constants.Reply,
                        ((int)Math.Round(mbApiInterface.Player_GetVolume() * 100, 1))).toJsonString()));

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
                    new SocketMessage(Constants.PlayerShuffle, Constants.Reply,
                        mbApiInterface.Player_GetShuffle()).toJsonString()));
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
                    new SocketMessage(Constants.PlayerMute, Constants.Reply,
                        mbApiInterface.Player_GetMute()).toJsonString()));
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
                    new SocketMessage(Constants.PlayerScrobble, Constants.Reply,
                        mbApiInterface.Player_GetScrobbleEnabled()).toJsonString()));
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
                    new SocketMessage(Constants.PlayerRepeat, Constants.Reply,
                        mbApiInterface.Player_GetRepeat()).toJsonString()));
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
                    new SocketMessage(Constants.NowPlayingList, Constants.Reply,
                        trackList).toJsonString(),clientId));
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
            if (!string.IsNullOrEmpty(rating) && (float.Parse(rating) >= 0 && float.Parse(rating) <= 5))
            {
                mbApiInterface.Library_SetFileTag(mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating, rating);
                mbApiInterface.Library_CommitTagsToFile(mbApiInterface.NowPlaying_GetFileUrl());
                mbApiInterface.Player_GetShowRatingTrack();
                mbApiInterface.MB_RefreshPanels();
            }


            if (!String.IsNullOrEmpty(clientId))
            {
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingRating, Constants.Reply,
                            mbApiInterface.Library_GetFileTag(
                            mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating)).toJsonString(), clientId));
            }
            else
            {
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingRating,Constants.Reply,
                            mbApiInterface.Library_GetFileTag(
                                mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating)).toJsonString()));
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
                        new SocketMessage(Constants.NowPlayingLyrics, Constants.Reply,
                            mbApiInterface.NowPlaying_GetLyrics()).toJsonString()));
            }
            else if (mbApiInterface.ApiRevision >= 17)
            {
                string lyrics = mbApiInterface.NowPlaying_GetDownloadedLyrics();
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingLyrics, Constants.Reply,
                            !String.IsNullOrEmpty(lyrics) ? lyrics : "Retrieving Lyrics").toJsonString()));
            }
            else
            {
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingLyrics, Constants.Reply,
                            "Lyrics Not Found").toJsonString()));
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
                                                    mbApiInterface.NowPlaying_GetArtwork(), "",
                                                    mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)));
            }
            else if (mbApiInterface.ApiRevision >= 17)
            {
                string cover = mbApiInterface.NowPlaying_GetDownloadedArtwork();
                if (!String.IsNullOrEmpty(cover))
                {
                    EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange, cover, "",
                                                        mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)));
                }
            }
            else
            {
                EventBus.FireEvent(new MessageEvent(EventType.NowPlayingCoverChange, String.Empty, "",
                                                    mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)));
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
                    new SocketMessage(Constants.NowPlayingPosition, Constants.Reply, position).toJsonString()));
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
                    new SocketMessage(Constants.NowPlayingListPlay, Constants.Reply,
                        result).toJsonString()));
         }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <param name="clientId"></param>
        public void NowPlayingListRemoveTrack(int index, string clientId)
        {
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListRemove, Constants.Reply,
                        mbApiInterface.NowPlayingList_RemoveAt(index)).toJsonString(), clientId));
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
                    new SocketMessage(Constants.PlayerAutoDj, Constants.Reply,
                        mbApiInterface.Player_GetAutoDjEnabled()).toJsonString()));
        }

        /// <summary>
        /// This function is used to change the playing track's last.fm love rating.
        /// </summary>
        /// <param name="action">
        /// The action can be either love, or ban.
        /// </param>
        public void RequestLoveStatusChange(string action)
        {
            switch (action)
            {
                case "love":
                    mbApiInterface.Library_SetFileTag(
                        mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.RatingLove, "Llfm");
                    break;
                case "ban":
                    mbApiInterface.Library_SetFileTag(
                        mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.RatingLove, "Blfm");
                    break;
            }
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
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingLfmRating, Constants.Reply, lastfmStatus).toJsonString()));
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
            PlayerStatus status = new PlayerStatus
                {
                    playerrepeat = mbApiInterface.Player_GetRepeat().ToString(),
                    playermute = mbApiInterface.Player_GetMute().ToString(),
                    playershuffle = mbApiInterface.Player_GetShuffle().ToString(),
                    scrobbler = mbApiInterface.Player_GetScrobbleEnabled().ToString(),
                    playerstate = mbApiInterface.Player_GetPlayState().ToString(),
                    playervolume =
                        ((int) Math.Round(mbApiInterface.Player_GetVolume()*100, 1)).ToString(
                            CultureInfo.InvariantCulture)
                };


            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.PlayerStatus, Constants.Reply, status).toJsonString(), clientId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientId"></param>
        public void RequestTrackInfo(string clientId)
        {
            EventBus.FireEvent(new MessageEvent(
                EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingTrack, Constants.Reply, GetTrackInfo()).toJsonString(), clientId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="data"></param>
        public void RequestNowPlayingMove(string clientId, string data)
        {
            bool result = false;
            string[] values = data.Split('#');
            int[] from = new int[1];
            from[0] = int.Parse(values[0]);
            int to = int.Parse(values[1]);
            result = mbApiInterface.NowPlayingList_MoveFiles(from, to);
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.NowPlayingListMove, Constants.Reply,result).toJsonString(), clientId));
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
                    new SocketMessage(Constants.LibrarySearchAlbum, Constants.Reply,
                        albumList).toJsonString(), clientId));
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
                    new SocketMessage(Constants.LibraryArtistAlbums, Constants.Reply,
                        albumList).toJsonString(), clientId));
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
                    new SocketMessage(Constants.LibrarySearchArtist, Constants.Reply,
                        artistList).toJsonString(),clientId));
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
                    new SocketMessage(Constants.LibraryGenreArtists, Constants.Reply,
                        artistList).toJsonString(), clientId));

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
                    new SocketMessage(Constants.LibrarySearchGenre, Constants.Reply,
                        genreList).toJsonString(), clientId));

            genreList = null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="title"></param>
        /// <param name="clientId"></param>
        public void LibrarySearchTitle(string title, string clientId)
        {
            List<Track> tracks = new List<Track>();
            if (mbApiInterface.Library_QueryLookupTable("title", "artist" + '\0' + "title",
                                                        XmlFilter(new[] {"Title"}, title, false)))
            {
                foreach (
                    string entry in
                        mbApiInterface.Library_QueryGetLookupTableValue(null)
                                      .Split(new[] {"\0\0"}, StringSplitOptions.None))
                {
                    string[] trackInfo = entry.Split(new[] {'\0'}, StringSplitOptions.None);

                    tracks.Add(trackInfo.Length == 3
                                   ? new Track(trackInfo[1], trackInfo[2])
                                   : new Track(trackInfo[0], trackInfo[1]));
                }
            }

            mbApiInterface.Library_QueryLookupTable(null, null, null);
            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibrarySearchTitle, Constants.Reply, tracks).toJsonString(),clientId));

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

                    trackList.Add(new Track(mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.Artist),
                                              mbApiInterface.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle), trackNumber));
                }
                trackList.Sort();
            }

            EventBus.FireEvent(
                new MessageEvent(EventType.ReplyAvailable,
                    new SocketMessage(Constants.LibraryAlbumTracks, Constants.Reply, trackList).toJsonString(), client));

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
            string filter = String.Empty;
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
                    filter = XmlFilter(new[] {"Title"}, query, true);
                    break;
                case MetaTag.none:
                    return;
                default:
                    return;
            }
            if (!mbApiInterface.Library_QueryFiles(filter)) return;

            List<string> trackList = new List<string>();
            while (true)
            {
                string current = mbApiInterface.Library_QueryGetNextFile();
                if (String.IsNullOrEmpty(current)) break;
                trackList.Add(current);
            }

            if (queue == QueueType.Next)
            {
                mbApiInterface.NowPlayingList_QueueFilesNext(trackList.ToArray());
            }
            else if (queue == QueueType.Last)
            {
                mbApiInterface.NowPlayingList_QueueFilesLast(trackList.ToArray());
            }
            else if (queue == QueueType.PlayNow)
            {
                mbApiInterface.NowPlayingList_Clear();
                mbApiInterface.NowPlayingList_QueueFilesNext(trackList.ToArray());
                mbApiInterface.NowPlayingList_PlayNow(trackList[0]);
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
                    new SocketMessage(Constants.NowPlayingListSearch, Constants.Reply,result).toJsonString(), clientId));
        }
    }
}