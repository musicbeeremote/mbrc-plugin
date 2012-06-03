using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security;
using MusicBeePlugin.Entities;
using MusicBeePlugin.Error;
using MusicBeePlugin.Events;

namespace MusicBeePlugin.Model
{
    internal class PlayerStateModel
    {
        public event EventHandler<DataEventArgs> ModelStateEvent;

        private TrackInfo _track;
        private string _cover;

        private string _lyrics;

        private Plugin.PlayState _playState;
        private int _volume;

        private bool _shuffleState;
        private Plugin.RepeatMode _repeatMode;
        private bool _muteState;
        private bool _scrobblerState;
        private int _trackRating;

        private void OnModelStateChange(DataEventArgs args)
        {
            EventHandler<DataEventArgs> handler = ModelStateEvent;
            if (handler != null) handler(this, args);
        }

        public int TrackRating
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

        public Plugin.RepeatMode RepeatMode
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
            set
            {
                _volume = value;
                OnModelStateChange(new DataEventArgs(EventDataType.Volume));
            }
        }

        public void setPlayState(Plugin.PlayState state)
        {
                _playState = state;
                OnModelStateChange(new DataEventArgs(EventDataType.PlayState));   
        }

        public string PlayState
        {
            get
            {
                switch (_playState)
                {
                    case Plugin.PlayState.Undefined:
                        return "undefined";
                    case Plugin.PlayState.Loading:
                        return "loading";
                    case Plugin.PlayState.Playing:
                        return "playing";
                    case Plugin.PlayState.Paused:
                        return "paused";
                    case Plugin.PlayState.Stopped:
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
                _track = value;
                OnModelStateChange(new DataEventArgs(EventDataType.Track));
            }
            get { return _track; }
        }


        public string Cover
        {
            set
            {
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
                    _lyrics = SecurityElement.Escape(lyricsString);
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
                    OnModelStateChange(new DataEventArgs(EventDataType.Lyrics));
                }
            }
            get { return _lyrics; }
        }
    }
}