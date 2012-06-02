using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Timers;
using MusicBeePlugin.Controller;
using MusicBeePlugin.Error;
using MusicBeePlugin.Events;
using MusicBeePlugin.Interfaces;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Settings;

namespace MusicBeePlugin
{
    /// <summary>
    /// The MusicBee Plugin class. Used to communicate with the MusicBee API.
    /// </summary>
    public partial class Plugin:IPlugin
    {
        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();
        private Timer _timer;
        private bool _shuffle;
        private RepeatMode _repeat;
        private bool _scrobble;

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
            _about.Name = "Remote Control: Server";
            _about.Description = "Used to manage MusicBee remotely though network.";
            _about.Author = "Kelsos";
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
            _about.ConfigurationPanelHeight = 50;

            _scrobble = _mbApiInterface.Player_GetScrobbleEnabled();
            _repeat = _mbApiInterface.Player_GetRepeat();
            _shuffle = _mbApiInterface.Player_GetShuffle();

            PlayerStateController.Instance.Initialize(this);
            ProtocolHandler.Instance.Initialize(this);

            ErrorHandler.SetLogFilePath(_mbApiInterface.Setting_GetPersistentStoragePath());

            CommunicationController.Instance.StartSocket();
            _timer = new Timer {Interval = 1000};
            _timer.Elapsed += HandleTimerElapsed;
            _timer.Enabled = true;

            return _about;
        }

        private void HandleTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_mbApiInterface.Player_GetShuffle() != _shuffle)
            {
                OnPlayerStateChanged(new DataEventArgs(DataType.ShuffleState));
                _shuffle = _mbApiInterface.Player_GetShuffle();
            }
            if (_mbApiInterface.Player_GetScrobbleEnabled() != _scrobble)
            {
                OnPlayerStateChanged(new DataEventArgs(DataType.ScrobblerState));
                _scrobble = _mbApiInterface.Player_GetScrobbleEnabled();
            }
            if (_mbApiInterface.Player_GetRepeat() != _repeat)
            {
                OnPlayerStateChanged(new DataEventArgs(DataType.RepeatMode));
                _repeat = _mbApiInterface.Player_GetRepeat();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="panelHandle"></param>
        /// <returns></returns>
        public bool Configure(IntPtr panelHandle)
        {
            SettingsMenuHandler handler = new SettingsMenuHandler();

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
          CommunicationController.Instance.StopSocket();
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
        /// Used to save the temporary Plugin Settings if the have changed.
        /// </summary>
        public void SaveSettings()
        {
            UserSettings.Settings = SettingsMenuHandler.Settings;
            UserSettings.SaveSettings("mbremote");
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
                    OnPlayerStateChanged(new DataEventArgs(DataType.Track));
                    break;
                case NotificationType.VolumeLevelChanged:
                    OnPlayerStateChanged(new DataEventArgs(DataType.Volume));
                    break;
                case NotificationType.VolumeMuteChanged:
                    OnPlayerStateChanged(new DataEventArgs(DataType.MuteState));
                    break;
                case NotificationType.PlayStateChanged:
                    OnPlayerStateChanged(new DataEventArgs(DataType.PlayState));
                    break;
            }
        }

        /// <summary>
        /// Returns the artist name for the track playing.
        /// </summary>
        /// <value> Track artist string </value>
        public string CurrentTrackArtist
        {
            get { return SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist)); }
        }

        /// <summary>
        /// Returns the album for the track playing.
        /// </summary>
        /// <value> Track album string </value>
        public string CurrentTrackAlbum
        {
            get { return SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album)); }
        }

        /// <summary>
        /// Returns the title for the track playing.
        /// </summary>
        /// <value> Track title string </value>
        public string CurrentTrackTitle
        {
            get { return SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle)); }
        }

        /// <summary>
        /// Returns the Year for the track playing.
        /// </summary>
        /// <value> Track year string </value>
        public string CurrentTrackYear
        {
            get { return _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Year); }
        }

        /// <summary>
        /// It retrieves the album cover as a Base64 encoded string for the track playing it resizes it to
        /// 300x300 and returns the resized image in a Base64 encoded string.
        /// </summary>
        /// <value> </value>
        public string CurrentTrackCover
        {
            get { return _mbApiInterface.NowPlaying_GetArtwork();}
        }

        /// <summary>
        /// Retrieves the lyrics for the track playing.
        /// </summary>
        /// <returns>Lyrics String</returns>
        public string CurrentTrackLyrics
        {
            get { return _mbApiInterface.NowPlaying_GetLyrics(); }
        }

        /// <summary>
        /// When called plays the next track.
        /// </summary>
        /// <returns></returns>
        public string PlayerPlayNextTrack()
        {
            return _mbApiInterface.Player_PlayNextTrack().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// When called stops the playback.
        /// </summary>
        /// <returns></returns>
        public string PlayerStopPlayback()
        {
            return _mbApiInterface.Player_Stop().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// When called changes the play/pause state or starts playing a track if the status is stopped.
        /// </summary>
        /// <returns></returns>
        public string PlayerPlayPauseTrack()
        {
            return _mbApiInterface.Player_PlayPause().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// When called plays the previous track.
        /// </summary>
        /// <returns></returns>
        public string PlayerPlayPreviousTrack()
        {
            return _mbApiInterface.Player_PlayPreviousTrack().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// When called if the volume string is an integer in the range [0,100] it 
        /// changes the volume to the specific value and returns the new value.
        /// In any other case it just returns the current value for the volume.
        /// </summary>
        /// <param name="vol">New volume String</param>
        /// <returns>Volume int [0,100]</returns>
        public string PlayerVolume(string vol)
        {
            int iVolume;
            if (int.TryParse(vol, out iVolume))
            {
                if (iVolume >= 0 && iVolume <= 100)
                {
                    _mbApiInterface.Player_SetVolume((float) iVolume/100);
                }
            }
            return ((int) Math.Round(_mbApiInterface.Player_GetVolume()*100, 1)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the state of the player.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <returns>possible values: undefined, loading, playing, paused, stopped</returns>
        public PlayState PlayerPlayState
        {
            get { return _mbApiInterface.Player_GetPlayState(); }
        }

        /// <summary>
        /// If the action equals toggle then it changes the shuffle state, in any other case
        /// it just returns the current value of the shuffle.
        /// </summary>
        /// <param name="action">toggle or state</param>
        /// <returns>Shuffle state: True or False</returns>
        public string PlayerShuffleState(string action)
        {
            if (action == "toggle")
                _mbApiInterface.Player_SetShuffle(!_mbApiInterface.Player_GetShuffle());
            return _mbApiInterface.Player_GetShuffle().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// If the action equals toggle then it changes the repeat state, in any other case
        /// it just returns the current value of the repeat.
        /// </summary>
        /// <param name="action">toggle or state</param>
        /// <returns>Repeat state: None, All, One</returns>
        public string PlayerRepeatState(string action)
        {
            if (action == "toggle")
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
            return _mbApiInterface.Player_GetRepeat().ToString();
        }

        /// <summary>
        /// If the action is toggle then the function changes the repeat state, in any other case
        /// it just returns the current value of the Mute.
        /// </summary>
        /// <param name="action">toggle or state</param>
        /// <returns>Mute state: True or False</returns>
        public string PlayerMuteState(string action)
        {
            if (action == "toggle")
                _mbApiInterface.Player_SetMute(!_mbApiInterface.Player_GetMute());
            return _mbApiInterface.Player_GetMute().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// It gets the 100 first tracks of the playlist and returns them in an XML formated String without a root element.
        /// </summary>
        /// <param name="clientProtocolVersion"> </param>
        /// <returns>XML formated string without root element</returns>
        public string PlaylistGetTracks(double clientProtocolVersion)
        {
            if (clientProtocolVersion>=1)
            {
                _mbApiInterface.NowPlayingList_QueryFiles(null);

                string songlist = "";
                int count = 0;
                while (count <= 500)
                {
                    string playListTrack = _mbApiInterface.NowPlayingList_QueryGetNextFile();
                    if (String.IsNullOrEmpty(playListTrack))
                        break;
                    songlist += "<playlistItem><artist>" +
                                SecurityElement.Escape(_mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.Artist)) +
                                "</artist><title>" +
                                SecurityElement.Escape(_mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.TrackTitle)) +
                                "</title></playlistItem>";
                    count++;
                }
                return songlist; 
            }
            return string.Empty;
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
        /// If the action is toggle then the function changes the scrobbler state, in any other case
        /// it just returns the current value of the Scrobbler.
        /// </summary>
        /// <param name="action">toggle or action</param>
        /// <returns>Scrobbler state</returns>
        public string ScrobblerState(string action)
        {
            if (action == "toggle")
                _mbApiInterface.Player_SetScrobbleEnabled(!_mbApiInterface.Player_GetScrobbleEnabled());
            return _mbApiInterface.Player_GetScrobbleEnabled().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// If the given rating string is not null or empty and the value of the string is a float number in the [0,5]
        /// the function will set the new rating as the current track's new track rating. In any other case it will
        /// just return the rating for the current track.
        /// </summary>
        /// <param name="rating">New Track Rating</param>
        /// <returns>Track Rating</returns>
        public string TrackRating(string rating)
        {
            if (!string.IsNullOrEmpty(rating) && (float.Parse(rating) >= 0 && float.Parse(rating) <= 5))
            {
                _mbApiInterface.Library_SetFileTag(_mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating, rating);
                _mbApiInterface.Library_CommitTagsToFile(_mbApiInterface.NowPlaying_GetFileUrl());
            }
            return _mbApiInterface.Library_GetFileTag(_mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating);
        }
    }
}