using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Timers;
using MusicBeePlugin.AndroidRemote.Controller;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Error;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin
{
    /// <summary>
    /// The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
    public partial class Plugin : IPlugin
    {
        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();
        private Timer _timer;
        private bool _shuffle;
        private RepeatMode _repeat;
        private bool _scrobble;

        /// <summary>
        /// Represents a change in the state of the player.
        /// </summary>
        public event EventHandler<DataEventArgs> PlayerStateChanged;

        private void OnPlayerStateChanged(DataEventArgs e)
        {
            EventHandler<DataEventArgs> handler = PlayerStateChanged;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// This function initialized the Plugin.
        /// </summary>
        /// <param name="apiInterfacePtr"></param>
        /// <returns></returns>
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _mbApiInterface =
                (MusicBeeApiInterface) Marshal.PtrToStructure(apiInterfacePtr, typeof (MusicBeeApiInterface));
            UserSettings.SettingsFilePath = _mbApiInterface.Setting_GetPersistentStoragePath();
            UserSettings.SettingsFileName = "mb_remote\\settings.xml";
            UserSettings.LoadSettings();
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "MusicBee Remote:Server";
            _about.Description = "Remote Control for server to be used with android application.";
            _about.Author = "Konstantinos Paparas (aka Kelsos)";
            _about.TargetApplication = "MusicBee Remote";
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            _about.Type = PluginType.General;
            _about.VersionMajor = Convert.ToInt16(v.Major);
            _about.VersionMinor = Convert.ToInt16(v.Minor);
            _about.Revision = Convert.ToInt16(v.Revision);
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            _about.ConfigurationPanelHeight = 200;

            RemoteController.Instance.Initialize(this);
            RemoteController.Instance.StartSocket();

            ErrorHandler.SetLogFilePath(_mbApiInterface.Setting_GetPersistentStoragePath());

            StartPlayerStatusMonitoring();

            return _about;
        }

        private void StartPlayerStatusMonitoring()
        {
            _scrobble = _mbApiInterface.Player_GetScrobbleEnabled();
            _repeat = _mbApiInterface.Player_GetRepeat();
            _shuffle = _mbApiInterface.Player_GetShuffle();
            _timer = new Timer {Interval = 1000};
            _timer.Elapsed += HandleTimerElapsed;
            _timer.Enabled = true;
        }

        private void HandleTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_mbApiInterface.Player_GetShuffle() != _shuffle)
            {
                _shuffle = _mbApiInterface.Player_GetShuffle();
                OnPlayerStateChanged(new DataEventArgs(EventDataType.ShuffleState, _shuffle));
            }
            if (_mbApiInterface.Player_GetScrobbleEnabled() != _scrobble)
            {
                _scrobble = _mbApiInterface.Player_GetScrobbleEnabled();
                OnPlayerStateChanged(new DataEventArgs(EventDataType.ScrobblerState, _scrobble));
            }
            if (_mbApiInterface.Player_GetRepeat() != _repeat)
            {
                _repeat = _mbApiInterface.Player_GetRepeat();
                OnPlayerStateChanged(new DataEventArgs(EventDataType.RepeatMode, Mapper.MapRepeatEnum(_repeat)));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="panelHandle"></param>
        /// <returns></returns>
        public bool Configure(IntPtr panelHandle)
        {
            SettingsController handler = new SettingsController();

            int background = _mbApiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl, ElementState.ElementStateDefault, ElementComponent.ComponentBackground);
            int foreground = _mbApiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl, ElementState.ElementStateDefault, ElementComponent.ComponentForeground);
            return handler.ConfigureSettingsPanel(panelHandle, background, foreground);
        }

        /// <summary>
        /// MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        /// </summary>
        /// <param name="reason"></param>
        public void Close(PluginCloseReason reason)
        {
            /** When the plugin closes for whatever reason the SocketServer must stop **/
            RemoteController.Instance.StopSocket();
        }

        /// <summary>
        /// Cleans up any persisted files during the plugin uninstall.
        /// </summary>
        public void Uninstall()
        {
            //TODO: add cleanup code bit to remove the plugin settings and log directory.
        }

        /// <summary>
        /// Called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        /// Used to save the temporary Plugin SettingsModel if the have changed.
        /// </summary>
        public void SaveSettings()
        {
            UserSettings.SettingsModel = SettingsController.SettingsModel;
            UserSettings.SaveSettings("mbremote");
            RemoteController.Instance.StopSocket();
            RemoteController.Instance.StartSocket();
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

                    TrackInfo track = new TrackInfo
                                          {
                                              Artist = SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist)),
                                              Album = SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)),
                                              Title = SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle)),
                                              Year = SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Year))
                                          };

                    OnPlayerStateChanged(new DataEventArgs(EventDataType.Track, track));
                    break;
                case NotificationType.VolumeLevelChanged:
                    OnPlayerStateChanged(new DataEventArgs(EventDataType.Volume, _mbApiInterface.Player_GetVolume()));
                    break;
                case NotificationType.VolumeMuteChanged:
                    OnPlayerStateChanged(new DataEventArgs(EventDataType.MuteState, _mbApiInterface.Player_GetMute()));
                    break;
                case NotificationType.PlayStateChanged:
                    OnPlayerStateChanged(new DataEventArgs(EventDataType.PlayState, Mapper.MapPlayStateEnum(_mbApiInterface.Player_GetPlayState())));
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    if (_mbApiInterface.ApiRevision >= 17)
                    {
                        OnPlayerStateChanged(new DataEventArgs(EventDataType.Lyrics, !String.IsNullOrEmpty(_mbApiInterface.NowPlaying_GetDownloadedLyrics()) ? _mbApiInterface.NowPlaying_GetDownloadedLyrics() : "Lyrics Not Found"));
                    }
                    break;
                case NotificationType.NowPlayingArtworkReady:
                    if (_mbApiInterface.ApiRevision >= 17)
                    {
                        OnPlayerStateChanged(new DataEventArgs(EventDataType.Cover, _mbApiInterface.NowPlaying_GetDownloadedArtwork()));
                    }
                    break;
              
            }
        }

        /// <summary>
        /// When called plays the next track.
        /// </summary>
        /// <returns></returns>
        public void RequestNextTrack(int clientId)
        {
            string reply = _mbApiInterface.Player_PlayNextTrack().ToString(CultureInfo.InvariantCulture);
            OnPlayerStateChanged(new DataEventArgs(EventDataType.NextTrackRequest, reply, clientId));
        }

        /// <summary>
        /// When called stops the playback.
        /// </summary>
        /// <returns></returns>
        public void RequestStopPlayback(int clientId)
        {
            string reply = _mbApiInterface.Player_Stop().ToString(CultureInfo.InvariantCulture);
            OnPlayerStateChanged(new DataEventArgs(EventDataType.StopRequest, reply, clientId));
        }

        /// <summary>
        /// When called changes the play/pause state or starts playing a track if the status is stopped.
        /// </summary>
        /// <returns></returns>
        public void RequestPlayPauseTrack(int clientId)
        {
            string reply = _mbApiInterface.Player_PlayPause().ToString(CultureInfo.InvariantCulture);
            OnPlayerStateChanged(new DataEventArgs(EventDataType.PlayPauseRequest, reply, clientId));
        }

        /// <summary>
        /// When called plays the previous track.
        /// </summary>
        /// <returns></returns>
        public void RequestPreviousTrack(int clientId)
        {
            string reply = _mbApiInterface.Player_PlayPreviousTrack().ToString(CultureInfo.InvariantCulture);
            OnPlayerStateChanged(new DataEventArgs(EventDataType.PreviousTrackRequest, reply, clientId));
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
                _mbApiInterface.Player_SetVolume((float) volume/100);
            }
            OnPlayerStateChanged(new DataEventArgs(EventDataType.Volume, _mbApiInterface.Player_GetVolume()));
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
                _mbApiInterface.Player_SetShuffle(!_mbApiInterface.Player_GetShuffle());
            }
            bool shuffleState = _mbApiInterface.Player_GetShuffle();
            OnPlayerStateChanged(new DataEventArgs(EventDataType.ShuffleState, shuffleState));
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
                _mbApiInterface.Player_SetMute(!_mbApiInterface.Player_GetMute());
            }
            bool muteState = _mbApiInterface.Player_GetMute();
            OnPlayerStateChanged(new DataEventArgs(EventDataType.MuteState, muteState));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        public void RequestScrobblerState(StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                _mbApiInterface.Player_SetScrobbleEnabled(!_mbApiInterface.Player_GetScrobbleEnabled());
            }
            bool scrobblerState = _mbApiInterface.Player_GetScrobbleEnabled();
            OnPlayerStateChanged(new DataEventArgs(EventDataType.ScrobblerState, scrobblerState));
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
                switch (_mbApiInterface.Player_GetRepeat())
                {
                    case RepeatMode.None:
                        _mbApiInterface.Player_SetRepeat(RepeatMode.All);
                        break;
                    case RepeatMode.All:
                        _mbApiInterface.Player_SetRepeat(RepeatMode.None);
                        break;
                    case RepeatMode.One:
                        _mbApiInterface.Player_SetRepeat(RepeatMode.None);
                        break;
                }
            }
            OnPlayerStateChanged(new DataEventArgs(EventDataType.RepeatMode, Mapper.MapRepeatEnum(_mbApiInterface.Player_GetRepeat())));
        }

        /// <summary>
        /// It gets the 100 first tracks of the playlist and returns them in an XML formated String without a root element.
        /// </summary>
        /// <param name="clientProtocolVersion"> </param>
        /// <param name="clientId"> </param>
        /// <returns>XML formated string without root element</returns>
        public void RequestNowPlayingList(double clientProtocolVersion, int clientId)
        {
            if (clientProtocolVersion >= 1)
            {
                _mbApiInterface.NowPlayingList_QueryFiles(null);

                string songlist = "";
                int count = 0;
                while (count <= 500)
                {
                    string playListTrack = _mbApiInterface.NowPlayingList_QueryGetNextFile();
                    if (String.IsNullOrEmpty(playListTrack))
                        break;
                    songlist += "<playlistItem><artist>" + SecurityElement.Escape(_mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.Artist)) +
                                "</artist><title>" + SecurityElement.Escape(_mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.TrackTitle)) +
                                "</title></playlistItem>";
                    count++;
                }

                OnPlayerStateChanged(new DataEventArgs(EventDataType.Playlist, songlist, clientId));
            }
        }

        /// <summary>
        /// Searches in the Now playing list for the track specified and plays it.
        /// </summary>
        /// <param name="trackInfo">The track to play</param>
        /// <returns></returns>
        public string PlaylistGoToSpecifiedTrack(string trackInfo)
        {
            string result = false.ToString(CultureInfo.InvariantCulture);
            string trackInformation = trackInfo.Replace(" - ", "\0");
            int index = trackInformation.IndexOf("\0", StringComparison.Ordinal);
            trackInformation = trackInformation.Substring(index + 1);
            _mbApiInterface.NowPlayingList_QueryFiles("*");
            string trackList = _mbApiInterface.NowPlayingList_QueryGetAllFiles();
            string[] tracks = trackList.Split("\0".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            foreach (string t in tracks)
            {
                if (_mbApiInterface.Library_GetFileTag(t, MetaDataType.TrackTitle) != trackInformation) continue;
                _mbApiInterface.NowPlayingList_PlayNow(t);
                result = true.ToString(CultureInfo.InvariantCulture);
                break;
            }
            return result;
        }

        /// <summary>
        /// If the given rating string is not null or empty and the value of the string is a float number in the [0,5]
        /// the function will set the new rating as the current track's new track rating. In any other case it will
        /// just return the rating for the current track.
        /// </summary>
        /// <param name="rating">New Track Rating</param>
        /// <param name="clientId"> </param>
        /// <returns>Track Rating</returns>
        public void RequestTrackRating(string rating, int clientId)
        {
            if (!string.IsNullOrEmpty(rating) && (float.Parse(rating) >= 0 && float.Parse(rating) <= 5))
            {
                _mbApiInterface.Library_SetFileTag(_mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating, rating);
                _mbApiInterface.Library_CommitTagsToFile(_mbApiInterface.NowPlaying_GetFileUrl());
            }
            OnPlayerStateChanged(new DataEventArgs(EventDataType.TrackRating, _mbApiInterface.Library_GetFileTag(_mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating), clientId));
        }

        /// <summary>
        /// Requests the Now Playing track lyrics. If the lyrics are available then they are dispatched along with
        /// and event. If not, and the ApiRevision is equal or greater than r17 a request for the downloaded lyrics
        /// is initiated. The lyrics are dispatched along with and event when ready.
        /// </summary>
        public void RequestNowPlayingTrackLyrics()
        {
            if (!String.IsNullOrEmpty(_mbApiInterface.NowPlaying_GetLyrics()))
            {
                OnPlayerStateChanged(new DataEventArgs(EventDataType.Lyrics, _mbApiInterface.NowPlaying_GetLyrics()));
            }
            else if (_mbApiInterface.ApiRevision >= 17)
            {
                string lyrics = _mbApiInterface.NowPlaying_GetDownloadedLyrics();
                OnPlayerStateChanged(new DataEventArgs(EventDataType.Lyrics, !String.IsNullOrEmpty(lyrics)?lyrics:"Retrieving Lyrics"));
            }
            else
            {
                OnPlayerStateChanged(new DataEventArgs(EventDataType.Lyrics, "Lyrics Not Found"));
            }
        }

        /// <summary>
        /// Requests the Now Playing Track Cover. If the cover is available it is dispatched along with an event.
        /// If not, and the ApiRevision is equal or greater than r17 a request for the downloaded artwork is
        /// initiated. The cover is dispatched along with an event when ready.
        /// </summary>
        public void RequestNowPlayingTrackCover()
        {
            if (!String.IsNullOrEmpty(_mbApiInterface.NowPlaying_GetArtwork()))
            {
                OnPlayerStateChanged(new DataEventArgs(EventDataType.Cover, _mbApiInterface.NowPlaying_GetArtwork()));
            }
            else if (_mbApiInterface.ApiRevision >= 17)
            {
                _mbApiInterface.NowPlaying_GetDownloadedArtwork();
            }
            else
            {
                OnPlayerStateChanged(new DataEventArgs(EventDataType.Cover, String.Empty));
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
                    _mbApiInterface.Player_SetPosition(newPosition);
                }
            }
            int currentPosition = _mbApiInterface.Player_GetPosition();
            int totalDuration = _mbApiInterface.NowPlaying_GetDuration();
            string data = string.Format("<current>{0}</current>" + "<duration>{1}</duration>", currentPosition, totalDuration);
            OnPlayerStateChanged(new DataEventArgs(EventDataType.PlaybackPosition,data));
        }

         public void RemoveTrackFromNowPlayingList(int index)
         {
             _mbApiInterface.NowPlayingList_RemoveAt(index);
         }
    }
}