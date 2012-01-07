using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();

        public bool SongChanged { get; set; }
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _mbApiInterface = (MusicBeeApiInterface)Marshal.PtrToStructure(apiInterfacePtr, typeof(MusicBeeApiInterface));
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "MusicBee Remote Control";
            _about.Description = "A plugin to allow music bee remote control through mobile applications and network.";
            _about.Author = "Kelsos";
            _about.TargetApplication = "MusicBee Remote";   // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            _about.Type = PluginType.General;
            _about.VersionMajor = 1;  // your plugin version
            _about.VersionMinor = 0;
            _about.Revision = 1;
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            _about.ConfigurationPanelHeight = 10;   // not implemented yet: height in pixels that MusicBee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
            SocketServer.Instance.ConnectToPlugin(this);
            SocketServer.Instance.Start();
            ErrorHandler.SetLogFilePath(_mbApiInterface.Setting_GetPersistentStoragePath());
            return _about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            var dataPath = _mbApiInterface.Setting_GetPersistentStoragePath();
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
                    // perform startup initialization
                    switch (_mbApiInterface.Player_GetPlayState())
                    {
                        case PlayState.Playing:
                        case PlayState.Paused:
                            // ...
                            break;
                    }
                    break;
                case NotificationType.TrackChanged:
                    SongChanged = true;
                    break;
            }
        }

        public string GetCurrentTrackArtist()
        {
            return _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
        }

        public string GetCurrentTrackAlbum()
        {
            return _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
        }

        public string GetCurrentTrackTitle()
        {
            return _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
        }

        public string GetCurrentTrackYear()
        {
            return _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Year);
        }

        public string GetCurrentTrackCover()
        {
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(_mbApiInterface.NowPlaying_GetArtwork())))
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
        
        // return lyrics for the requested artist/title
        // only required if PluginType = LyricsRetrieval
        // return null if no lyrics are found
        public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album, bool synchronisedPreferred)
        {
            return null;
        }

        public string RetrieveCurrentTrackLyrics()
        {
            //return _mbApiInterface.Library_GetLyrics(_mbApiInterface.NowPlaying_GetFileUrl(), 0);
            Debug.Write("Lyrics:"+_mbApiInterface.NowPlaying_GetLyrics());
            return _mbApiInterface.NowPlaying_GetLyrics();
        }

        // return Base64 string representation of the artwork binary data
        // only required if PluginType = ArtworkRetrieval
        // return null if no artwork is found
        public string RetrieveArtwork(string sourceFileUrl, string albumArtist, string album)
        {
            //return Convert.ToBase64String(artworkBinaryData)
            return null;
        }
        public void PlayerPlayNextTrack()
        {
            _mbApiInterface.Player_PlayNextTrack();
        }
        public void PlayerStopPlayback()
        {
            _mbApiInterface.Player_Stop();
        }
        public void PlayerPlayPauseTrack()
        {
            _mbApiInterface.Player_PlayPause();
        }
        public void PlayerPlayPreviousTrack()
        {
            _mbApiInterface.Player_PlayPreviousTrack();
        }

        public int PlayerVolume(String vol)
        {
            int iVolume;
            if (int.TryParse(vol, out iVolume))
            {
                if (iVolume >= 0 && iVolume <= 100)
                {
                    _mbApiInterface.Player_SetVolume((float)iVolume / 100);
                }
            }
            return (int)Math.Round(_mbApiInterface.Player_GetVolume() * 100, 1);
        }

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
        public string PlayerShuffleState(string action)
        {
            if (action == "toggle")
                _mbApiInterface.Player_SetShuffle(!_mbApiInterface.Player_GetShuffle());
            return _mbApiInterface.Player_GetShuffle().ToString();
        }

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
        public string PlayerMuteState(string action)
        {
            if (action == "toggle")
                _mbApiInterface.Player_SetMute(!_mbApiInterface.Player_GetMute());
            return _mbApiInterface.Player_GetMute().ToString();
        }
        public string PlaylistGetTracks()
        {
            _mbApiInterface.NowPlayingList_QueryFiles("*");
            string[] playListTracks = _mbApiInterface.NowPlayingList_QueryGetAllFiles().Split("\0".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            string songlist = "";
            if (playListTracks.Length <= 100)
            {
                foreach (var playListTrack in playListTracks)
                {
                    songlist += "<playlistItem><artist>" + _mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.Artist) + "</artist><title>" +
                           _mbApiInterface.Library_GetFileTag(playListTrack, MetaDataType.TrackTitle) + "</title></playlistItem>";

                }
            }
            else
            {
                for (int i = 0; i < 100; i++)
                {
                    songlist += "<playlistItem><artist>" + _mbApiInterface.Library_GetFileTag(playListTracks[i], MetaDataType.Artist) + "</artist><title>" +
                            _mbApiInterface.Library_GetFileTag(playListTracks[i], MetaDataType.TrackTitle) + "</title></playlistItem>";
                }
            }
            return songlist;

        }

        public void PlaylistGoToSpecifiedTrack(string trackInfo)
        {
            string trackInformation = trackInfo.Replace(" - ", "\0");
            int index = trackInformation.IndexOf("\0");
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
        public string ScrobblerState(string action)
        {
            if (action == "toggle")
                _mbApiInterface.Player_SetScrobbleEnabled(!_mbApiInterface.Player_GetScrobbleEnabled());
            return _mbApiInterface.Player_GetScrobbleEnabled().ToString();
        }

        public string TrackRating(string rating)
        {
            if (!string.IsNullOrEmpty(rating)&&(float.Parse(rating)>=0&&float.Parse(rating)<=5))
            {
                Debug.WriteLine("Passed");
               bool tag = _mbApiInterface.Library_SetFileTag(_mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating,
                                                   rating);
                _mbApiInterface.Library_CommitTagsToFile(_mbApiInterface.NowPlaying_GetFileUrl());
            }
            return _mbApiInterface.Library_GetFileTag(_mbApiInterface.NowPlaying_GetFileUrl(), MetaDataType.Rating);
        }
    }
}