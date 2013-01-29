using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.AndroidRemote;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Security;
    using System.Timers;
    using System.Windows.Forms;
    using AndroidRemote.Controller;
    using AndroidRemote.Entities;
    using AndroidRemote.Error;
    using AndroidRemote.Settings;
    using AndroidRemote.Utilities;

    using MusicBeePlugin.AndroidRemote.Enumerations;

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

            mbApiInterface.MB_AddMenuItem("mnuTools/MusicBee Remote", "Information Panel of the MusicBee Remote", MenuItemClicked);

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
            timer = new Timer { Interval = 1000 };
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
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateShuffleChanged));
            }

            if (mbApiInterface.Player_GetScrobbleEnabled() != scrobble)
            {
                scrobble = mbApiInterface.Player_GetScrobbleEnabled();
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateScrobbleChanged));
            }

            if (mbApiInterface.Player_GetRepeat() != repeat)
            {
                repeat = mbApiInterface.Player_GetRepeat();
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateRepeatChanged));
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
            if(Directory.Exists(settingsFolder))
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
                    TrackInfo track = GetTrackInfo();
                    RequestNowPlayingTrackCover();
                    RequestTrackRating(String.Empty,String.Empty);
                    EventBus.FireEvent(new MessageEvent(EventType.PlayerStateTrackChanged, track.ToXmlString()));
                    break;
                case NotificationType.VolumeLevelChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.PlayerStateVolumeChanged, ((int)Math.Round(mbApiInterface.Player_GetVolume() * 100, 1)).ToString(CultureInfo.InvariantCulture)));
                    break;
                case NotificationType.VolumeMuteChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.PlayerStateMuteChanged, mbApiInterface.Player_GetMute().ToString()));
                    break;
                case NotificationType.PlayStateChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.PlayerStatePlayStateChanged,mbApiInterface.Player_GetPlayState().ToString()));
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    if (mbApiInterface.ApiRevision >= 17)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.PlayerStateLyricsChanged, !String.IsNullOrEmpty(mbApiInterface.NowPlaying_GetDownloadedLyrics()) ? mbApiInterface.NowPlaying_GetDownloadedLyrics() : "Lyrics Not Found"));
                    }
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    if (mbApiInterface.ApiRevision >= 17)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.PlayerStateCoverChanged, mbApiInterface.NowPlaying_GetDownloadedArtwork(), "", mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)));
                    }
                    break;
                case NotificationType.NowPlayingListChanged:
                    EventBus.FireEvent(new MessageEvent(EventType.PlayerStateNowPlayingListChanged));
                    break;
            }
        }

        private TrackInfo GetTrackInfo()
        {
            TrackInfo track = new TrackInfo
                                  {
                                      Artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist),
                                      Album = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album),
                                      Year = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Year)
                                  };
            track.SetTitle(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle), mbApiInterface.NowPlaying_GetFileUrl());
            return track;
        }

        /// <summary>
        /// When called plays the next track.
        /// </summary>
        /// <returns></returns>
        public void RequestNextTrack(string clientId)
        {
            string reply = mbApiInterface.Player_PlayNextTrack().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// When called stops the playback.
        /// </summary>
        /// <returns></returns>
        public void RequestStopPlayback(string clientId)
        {
            string reply = mbApiInterface.Player_Stop().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// When called changes the play/pause state or starts playing a track if the status is stopped.
        /// </summary>
        /// <returns></returns>
        public void RequestPlayPauseTrack(string clientId)
        {
            string reply = mbApiInterface.Player_PlayPause().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// When called plays the previous track.
        /// </summary>
        /// <returns></returns>
        public void RequestPreviousTrack(string clientId)
        {
            string reply = mbApiInterface.Player_PlayPreviousTrack().ToString(CultureInfo.InvariantCulture);
            //EventBus.FireEvent(new MessageEvent(EventType.));
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

            string volStr = ((int)Math.Round(mbApiInterface.Player_GetVolume() * 100, 1)).ToString(CultureInfo.InvariantCulture);
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateVolumeChanged, volStr));

            if(mbApiInterface.Player_GetMute())
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
            string shuffleState = mbApiInterface.Player_GetShuffle().ToString();
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateShuffleChanged, shuffleState));
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
            string muteState = mbApiInterface.Player_GetMute().ToString();
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateMuteChanged, muteState));            
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
            string scrobblerState = mbApiInterface.Player_GetScrobbleEnabled().ToString();
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateScrobbleChanged,scrobblerState));            
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
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateRepeatChanged,mbApiInterface.Player_GetRepeat().ToString()));  
        }

        /// <summary>
        /// It gets the 100 first tracks of the playlist and returns them in an XML formated String without a root element.
        /// </summary>
        /// <param name="clientProtocolVersion"> </param>
        /// <param name="clientId"> </param>
        /// <returns>XML formated string without root element</returns>
        public void RequestNowPlayingList(double clientProtocolVersion, string clientId)
        {
            if (clientProtocolVersion >= 1)
            {
                mbApiInterface.NowPlayingList_QueryFiles(null);

                string songlist = "";
                int count = 0;
                while (count <= UserSettings.Instance.NowPlayingListLimit)
                {
                    string playListTrack = mbApiInterface.NowPlayingList_QueryGetNextFile();
                    if (String.IsNullOrEmpty(playListTrack))
                        break;

                    string artist = mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.Artist);
                    string title = mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.TrackTitle);

                    if(String.IsNullOrEmpty(artist))
                    {
                        artist = "Unknown Artist";
                    }

                    if(String.IsNullOrEmpty(title))
                    {
                        int index = playListTrack.LastIndexOf('\\');
                        title = playListTrack.Substring(index+1);
                    }

                    title = SecurityElement.Escape(title);
                    artist = SecurityElement.Escape(artist);
                    string song = XmlCreator.Create(Constants.Artist, artist, false, false);
                    song += XmlCreator.Create(Constants.Title, title, false, false);
                    songlist += XmlCreator.Create(Constants.PlaylistItem, song, false, false);
                    count++;
                }

                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateNowPlayingListData, songlist, clientId));
            }
        }

        /// <summary>
        /// Searches in the Now playing list for the track specified and plays it.
        /// </summary>
        /// <param name="index">The track to play</param>
        /// <returns></returns>
        public string NowPlayingPlay(string index)
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
            return result.ToString();
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
                    new MessageEvent(
                        EventType.PlayerStateRatingChanged,
                        this.mbApiInterface.Library_GetFileTag(
                            this.mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating),
                        clientId));
            }
            else
            {
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateRatingChanged, this.mbApiInterface.Library_GetFileTag(this.mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating)));    
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
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateLyricsChanged, mbApiInterface.NowPlaying_GetLyrics()));
            }
            else if (mbApiInterface.ApiRevision >= 17)
            {
                string lyrics = mbApiInterface.NowPlaying_GetDownloadedLyrics();
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateLyricsChanged, !String.IsNullOrEmpty(lyrics) ? lyrics : "Retrieving Lyrics"));
            }
            else
            {
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateLyricsChanged, "Lyrics Not Found"));
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
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateCoverChanged, mbApiInterface.NowPlaying_GetArtwork(), "", mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)));
            }
            else if (mbApiInterface.ApiRevision >= 17)
            {
                string cover = mbApiInterface.NowPlaying_GetDownloadedArtwork();
                if(!String.IsNullOrEmpty(cover))
                {
                    EventBus.FireEvent(new MessageEvent(EventType.PlayerStateCoverChanged, cover, "", mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)));
                }
            }
            else
            {
                EventBus.FireEvent(new MessageEvent(EventType.PlayerStateCoverChanged, String.Empty, "", mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)));
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
            string position = string.Format("<current>{0}</current>" + "<duration>{1}</duration>", currentPosition, totalDuration);
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStatePlaybackPositionChanged, position));
        }

         /// <summary>
         /// 
         /// </summary>
         /// <param name="index"></param>
         /// <param name="clientId"></param>
         public void NowPlayingListRemoveTrack(int index, string clientId)
         {
             bool trackRemoved = mbApiInterface.NowPlayingList_RemoveAt(index);
             string result = (trackRemoved ? index : -1).ToString(CultureInfo.InvariantCulture);
             EventBus.FireEvent(new MessageEvent(EventType.PlayerStateNowPlayingTrackRemoved,result,clientId));
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
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateAutoDjChanged, mbApiInterface.Player_GetAutoDjEnabled().ToString()));
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
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateLfmLoveRatingChanged, lastfmStatus.ToString()));
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
            PlayerStatus pStatus = new PlayerStatus
                                       {
                                           MuteState = mbApiInterface.Player_GetMute().ToString(),
                                           RepeatState = mbApiInterface.Player_GetRepeat().ToString(),
                                           Volume = ((int)Math.Round(mbApiInterface.Player_GetVolume() * 100, 1)).ToString(CultureInfo.InvariantCulture),
                                           PlayState = mbApiInterface.Player_GetPlayState().ToString(),
                                           ScrobblerState = mbApiInterface.Player_GetScrobbleEnabled().ToString(),
                                           ShuffleState = mbApiInterface.Player_GetShuffle().ToString()
                                       };
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateStatus,pStatus.ToXmlString(),clientId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientId"></param>
        public void RequestTrackInfo(string clientId)
        {
            TrackInfo track = GetTrackInfo();
            EventBus.FireEvent(new MessageEvent(EventType.PlayerStateTrackChanged, track.ToXmlString(), clientId));
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
        }

        public void RequestLibraryData(string clientId, string searchParam)
        {
            mbApiInterface.Library_QueryFiles(searchParam);
            while (true)
            {
                
                string track = mbApiInterface.Library_QueryGetNextFile();
                if (track==null)
                {
                    break;
                }
                Debug.WriteLine(track);

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        public void RequestLibraryAllArtists(string client)
        {
            List<string> artistList = new List<string>();
            mbApiInterface.Library_QueryFiles(null);

            while (true)
            {
                string file = mbApiInterface.Library_QueryGetNextFile();
                if(file == null) break;
                string artist = mbApiInterface.Library_GetFileTag(file, MetaDataType.Artist);
                if(!artistList.Contains(artist))
                {
                    artistList.Add(artist);
                }
            }

            string xml = artistList.Aggregate(string.Empty, (current, artist) => current + ("<artist><name>" + artist + "</name></artist>"));

            EventBus.FireEvent(new MessageEvent(EventType.LibraryArtistListReady, xml, client));

            RequestLibraryAllAlbums("d","Shinedown");
        }

        public void RequestLibraryAllAlbums(string client, string artist)
        {
            List<string> albumList = new List<string>();
            mbApiInterface.Library_QueryFiles("artist=" + artist);

            while (true)
            {
                string file = mbApiInterface.Library_QueryGetNextFile();
                if (String.IsNullOrEmpty(file)) break;
                string album = mbApiInterface.Library_GetFileTag(file, MetaDataType.Album);
                if(!albumList.Contains(album))
                {
                    albumList.Add(album);
                }
            }

            foreach (var al in albumList)
            {
                Debug.WriteLine(al);
            }
        }
    }
}