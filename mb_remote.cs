using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Timers;
using MusicBeePlugin.Events;
using MusicBeePlugin.Settings;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();
        private Timer _timer;
        private bool _shuffle;
        private RepeatMode _repeat;
        private bool _scrobble;

        private bool _songChanged;

        public bool SongChanged
        {
            get
            {
                if (_songChanged)
                {
                    bool songCh = _songChanged;
                    _songChanged = !_songChanged;
                    return songCh;
                }
                return _songChanged;
            }
            set { _songChanged = value; }
        }

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


            ProtocolHandler.Instance.Initialize(this);

            ErrorHandler.SetLogFilePath(_mbApiInterface.Setting_GetPersistentStoragePath());

            SocketServer.Instance.Start();
            _timer = new Timer {Interval = 1000};
            _timer.Elapsed += HandleTimerElapsed;
            _timer.Enabled = true;


            return _about;
        }

        private void HandleTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_mbApiInterface.Player_GetShuffle() != _shuffle)
            {
                Messenger.Instance.OnShuffleStateChanged(null);
                _shuffle = _mbApiInterface.Player_GetShuffle();
            }
            if (_mbApiInterface.Player_GetScrobbleEnabled() != _scrobble)
            {
                Messenger.Instance.OnScrobbleStateChanged(null);
                _scrobble = _mbApiInterface.Player_GetScrobbleEnabled();
            }
            if (_mbApiInterface.Player_GetRepeat() != _repeat)
            {
                Messenger.Instance.OnRepeatStateChanged(null);
                _repeat = _mbApiInterface.Player_GetRepeat();
            }
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            SettingsMenuHandler handler = new SettingsMenuHandler();

            int background = _mbApiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl,
                                                                         ElementState.ElementStateDefault,
                                                                         ElementComponent.
                                                                             ComponentBackground);
            int foreground = _mbApiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl,
                                                                          ElementState.ElementStateDefault,
                                                                          ElementComponent.
                                                                              ComponentForeground);
            return handler.ConfigureSettingsPanel(panelHandle, background, foreground);
        }


        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            SocketServer.Instance.Stop();
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }

        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            UserSettings.Settings = SettingsMenuHandler.Settings;
            UserSettings.SaveSettings("mbremote");
        }

        // receive event notifications from MusicBee
        // only required if about.ReceiveNotificationFlags = PlayerEvents
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            // perform some action depending on the notification type
            switch (type)
            {
                case NotificationType.PluginStartup:
                    break;
                case NotificationType.TrackChanged:
                    SongChanged = true;
                    Messenger.Instance.OnTrackChanged(null);
                    break;
                case NotificationType.VolumeLevelChanged:
                    Messenger.Instance.OnVolumeLevelChanged(null);
                    break;
                case NotificationType.VolumeMuteChanged:
                    Messenger.Instance.OnVolumeMuteChanged(null);
                    break;
                case NotificationType.PlayStateChanged:
                    Messenger.Instance.OnPlayStateChanged(null);
                    break;
            }
        }

        /// <summary>
        /// Returns the artist name for the track playing.
        /// </summary>
        /// <returns>Track artist string</returns>
        public string GetCurrentTrackArtist()
        {
            return SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist));
        }

        /// <summary>
        /// Returns the album for the track playing.
        /// </summary>
        /// <returns>Track album string</returns>
        public string GetCurrentTrackAlbum()
        {
            return SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album));
        }

        /// <summary>
        /// Returns the title for the track playing.
        /// </summary>
        /// <returns>Track title string</returns>
        public string GetCurrentTrackTitle()
        {
            return SecurityElement.Escape(_mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
        }

        /// <summary>
        /// Returns the Year for the track playing.
        /// </summary>
        /// <returns>Track year string</returns>
        public string GetCurrentTrackYear()
        {
            return _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Year);
        }

        /// <summary>
        /// It retrieves the album cover as a Base64 encoded string for the track playing it resizes it to
        /// 300x300 and returns the resized image in a Base64 encoded string.
        /// </summary>
        /// <returns></returns>
        public string GetCurrentTrackCover()
        {
            try
            {
                if (String.IsNullOrEmpty(_mbApiInterface.NowPlaying_GetArtwork()))
                    return "";
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(_mbApiInterface.NowPlaying_GetArtwork()))
                    )
                using (Image albumCover = Image.FromStream(ms, true))
                {
                    ms.Flush();
                    int sourceWidth = albumCover.Width;
                    int sourceHeight = albumCover.Height;

                    float nPercentW = (300 / (float)sourceWidth);
                    float nPercentH = (300 / (float)sourceHeight);

                    var nPercent = nPercentH < nPercentW ? nPercentH : nPercentW;
                    int destWidth = (int)(sourceWidth * nPercent);
                    int destHeight = (int)(sourceHeight * nPercent);
                    using (var bmp = new Bitmap(destWidth, destHeight))
                    using (MemoryStream ms2 = new MemoryStream())
                    {
                        Graphics graph = Graphics.FromImage(bmp);
                        graph.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graph.DrawImage(albumCover, 0, 0, destWidth, destHeight);
                        graph.Dispose();

                        bmp.Save(ms2, System.Drawing.Imaging.ImageFormat.Png);
                        bmp.Dispose();
                        return Convert.ToBase64String(ms2.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError(ex);
                return String.Empty;
            }
        }

        /// <summary>
        /// Retrieves the lyrics for the track playing.
        /// </summary>
        /// <returns>Lyrics String</returns>
        public string RetrieveCurrentTrackLyrics()
        {
            string lyricsString = _mbApiInterface.NowPlaying_GetLyrics().Trim();
            if (lyricsString.Contains("\r\r\n\r\r\n"))
            {
                lyricsString = lyricsString.Replace("\r\r\n\r\r\n", " &lt;p&gt; ").Replace("\r\r\n", " &lt;br&gt; ");
            }

            return
                SecurityElement.Escape(lyricsString.Replace("\0", " ").Replace("\r\n", "&lt;p&gt;").Replace("\n",
                                                                                                            "&lt;br&gt;"));
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
        /// <returns>possible values: undefined, loading, playing, paused, stopped</returns>
        public string PlayerPlayState()
        {
            switch (_mbApiInterface.Player_GetPlayState())
            {
                case PlayState.Undefined:
                    return "undefined";
                case PlayState.Loading:
                    return "loading";
                case PlayState.Playing:
                    return "playing";
                case PlayState.Paused:
                    return "paused";
                case PlayState.Stopped:
                    return "stopped";
                default:
                    throw new ArgumentOutOfRangeException();
            }
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
        /// <returns>XML formated string without root element</returns>
        public string PlaylistGetTracks()
        {
            _mbApiInterface.NowPlayingList_QueryFiles(null);


            string songlist = "";
            int count = 0;
            while (true && count <= 500)
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

            for (int i = 0; i < tracks.Length; i++)
            {
                if (_mbApiInterface.Library_GetFileTag(tracks[i], MetaDataType.TrackTitle) == trackInformation)
                {
                    _mbApiInterface.NowPlayingList_PlayNow(tracks[i]);
                    result = true.ToString(CultureInfo.InvariantCulture);
                    break;
                }
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
                _mbApiInterface.Library_SetFileTag(_mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating,
                                                   rating);
                _mbApiInterface.Library_CommitTagsToFile(_mbApiInterface.NowPlaying_GetFileUrl());
            }
            return _mbApiInterface.Library_GetFileTag(_mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating);
        }
    }
}