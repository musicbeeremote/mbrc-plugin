using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Error;
using MusicBeePlugin.AndroidRemote.Events;

namespace MusicBeePlugin.AndroidRemote.Model
{
    internal class PlayerStateModel
    {
        public event EventHandler<DataEventArgs> ModelStateEvent;

        private TrackInfo _track;
        private string _previousAlbum;
        private string _cover;

        private string _lyrics;

        private PlayerState _playerState;
        private int _volume;

        private bool _shuffleState;
        private Repeat _repeatMode;
        private bool _muteState;
        private bool _scrobblerState;
        private string _trackRating;


    private void OnModelStateChange(DataEventArgs args)
        {
            EventHandler<DataEventArgs> handler = ModelStateEvent;
            if (handler != null) handler(this, args);
        }

        public string TrackRating
        {
            get { return _trackRating; }
            set
            {
                _trackRating = value;
                OnModelStateChange(new DataEventArgs(EventDataType.TrackRating));
            }
        }

        public bool ScrobblerState
        {
            get { return _scrobblerState; }
            set
            {
                _scrobblerState = value;
                OnModelStateChange(new DataEventArgs(EventDataType.ScrobblerState));
            }
        }

        public bool MuteState
        {
            get { return _muteState; }
            set
            {
                _muteState = value;
                OnModelStateChange(new DataEventArgs(EventDataType.MuteState));
            }
        }

        public Repeat RepeatMode
        {
            get { return _repeatMode; }
            set
            {
                _repeatMode = value;
                OnModelStateChange(new DataEventArgs(EventDataType.RepeatMode));
            }
        }

        public bool ShuffleState
        {
            get { return _shuffleState; }
            set
            {
                _shuffleState = value;
                OnModelStateChange(new DataEventArgs(EventDataType.ShuffleState));
            }
        }

        public int Volume
        {
            get { return _volume; }
        }

        public void SetVolume(float volume)
        {
            _volume = ((int) Math.Round(volume*100, 1));
            OnModelStateChange(new DataEventArgs(EventDataType.Volume));
        }

        public void SetPlayState(PlayerState state)
        {
                _playerState = state;
                OnModelStateChange(new DataEventArgs(EventDataType.PlayState));   
        }

        public string PlayerState
        {
            get
            {
                switch (_playerState)
                {
                    case Enumerations.PlayerState.Undefined:
                        return "undefined";
                    case Enumerations.PlayerState.Loading:
                        return "loading";
                    case Enumerations.PlayerState.Playing:
                        return "playing";
                    case Enumerations.PlayerState.Paused:
                        return "paused";
                    case Enumerations.PlayerState.Stopped:
                        return "stopped";
                    default:
                        return "undefined";
                }
            }
        }

        public TrackInfo Track
        {
            set
            {
                _previousAlbum = _previousAlbum==null ? value.Album : _track.Album;
                _track = value;    
                OnModelStateChange(new DataEventArgs(EventDataType.Track));
            }
            get { return _track; }
        }


        public string Cover
        {
            set
            {
                if (!String.IsNullOrEmpty(_previousAlbum) && _previousAlbum.Equals(_track.Album)) return;
                try
                {
                    if (String.IsNullOrEmpty(value))
                    {
                        _cover = string.Empty;
                        return;
                    }
                    using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(value))
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
                            _cover = Convert.ToBase64String(ms2.ToArray());
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError(ex);
                    _cover = String.Empty;
                }
                finally
                {
                    OnModelStateChange(new DataEventArgs(EventDataType.Cover));
                }
            }
            get { return _cover; }
        }

        public string Lyrics
        {
            set
            {
                try
                {
                    string lyricsString = value.Trim();
                    if (lyricsString.Contains("\r\r\n\r\r\n"))
                    {
                        /* Convert new line & empty line to xml safe format */
                        lyricsString = lyricsString.Replace("\r\r\n\r\r\n", " &lt;p&gt; ");
                        lyricsString = lyricsString.Replace("\r\r\n", " &lt;br&gt; ");
                    }
                    lyricsString = lyricsString.Replace("\0", " ");
                    lyricsString = lyricsString.Replace("\r\n", "&lt;p&gt;");
                    lyricsString = lyricsString.Replace("\n", "&lt;br&gt;");
                    const string pattern = "\\[\\d:\\d{2}.\\d{3}\\] "; 
                    Regex regEx = new Regex(pattern);
                    _lyrics = SecurityElement.Escape(regEx.Replace(lyricsString,String.Empty));
                }
                catch (Exception ex)
                {
#if DEBUG
                    ErrorHandler.LogError(ex);
#endif
                    _lyrics = String.Empty;
                }
                finally
                {
                    if(!String.IsNullOrEmpty(_lyrics))
                        OnModelStateChange(new DataEventArgs(EventDataType.Lyrics));
                }
            }
            get { return _lyrics; }
        }
    }
}