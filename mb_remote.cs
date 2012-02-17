using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();

        public bool SongChanged { get; set; }

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _mbApiInterface =
                (MusicBeeApiInterface) Marshal.PtrToStructure(apiInterfacePtr, typeof (MusicBeeApiInterface));
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "MusicBee Remote Control";
            _about.Description = "A plugin to allow music bee remote control through mobile applications and network.";
            _about.Author = "Kelsos";
            _about.TargetApplication = "MusicBee Remote";
            // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            _about.Type = PluginType.General;
            _about.VersionMajor = 1; // your plugin version
            _about.VersionMinor = 0;
            _about.Revision = 1;
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            _about.ConfigurationPanelHeight = 10;

            SocketServer.Instance.ConnectToPlugin(this);
            SocketServer.Instance.Start();
            ErrorHandler.SetLogFilePath(_mbApiInterface.Setting_GetPersistentStoragePath());
            return _about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            // var dataPath = _mbApiInterface.Setting_GetPersistentStoragePath();
            return false;
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
            if (String.IsNullOrEmpty(_mbApiInterface.NowPlaying_GetArtwork()))
                return "";
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(_mbApiInterface.NowPlaying_GetArtwork()))
                )
            using (Image albumCover = Image.FromStream(ms, true))
            {
                ms.Flush();
                int sourceWidth = albumCover.Width;
                int sourceHeight = albumCover.Height;

                float nPercentW = (300/(float) sourceWidth);
                float nPercentH = (300/(float) sourceHeight);

                var nPercent = nPercentH < nPercentW ? nPercentH : nPercentW;
                int destWidth = (int) (sourceWidth*nPercent);
                int destHeight = (int) (sourceHeight*nPercent);
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
                SecurityElement.Escape(lyricsString.Replace("\0", " ").Replace("\r\n", "&lt;p&gt;").Replace("\n", "&lt;br&gt;"));
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
            _mbApiInterface.NowPlayingList_QueryFiles("*");
            string[] playListTracks = _mbApiInterface.NowPlayingList_QueryGetAllFiles().Split("\0".ToCharArray(),
                                                                                              StringSplitOptions.
                                                                                                  RemoveEmptyEntries);
            string songlist = "";
            if (playListTracks.Length <= 100)
            {
                foreach (var playListTrack in playListTracks)
                {
                    songlist += "<playlistItem><artist>" +
                                _mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.Artist) +
                                "</artist><title>" +
                                _mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.TrackTitle) +
                                "</title></playlistItem>";
                }
            }
            else
            {
                for (int i = 0; i < 100; i++)
                {
                    songlist += "<playlistItem><artist>" +
                                _mbApiInterface.Library_GetFileTag(playListTracks[i], MetaDataType.Artist) +
                                "</artist><title>" +
                                _mbApiInterface.Library_GetFileTag(playListTracks[i], MetaDataType.TrackTitle) +
                                "</title></playlistItem>";
                }
            }
            return songlist;
        }

        /// <summary>
        /// Searches in the Now playing list for the track specified and plays it.
        /// </summary>
        /// <param name="trackInfo">The track to play</param>
        /// <returns></returns>
        public void PlaylistGoToSpecifiedTrack(string trackInfo)
        {
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
                    break;
                }
            }
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