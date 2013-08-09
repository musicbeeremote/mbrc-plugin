using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Model
{
    using System;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.IO;
    using System.Security;
    using System.Text.RegularExpressions;
    using Error;
    using Events;

    internal class LyricCoverModel
    {
        /** Singleton **/
        private static readonly LyricCoverModel Model = new LyricCoverModel();

        private string previousAlbum;
        private string cover;
        private string lyrics;

        public static LyricCoverModel Instance
        {
            get { return Model; }
        }

        private LyricCoverModel()
        {
            
        }

        public void SetCover(string base64, string album)
        {
            if(previousAlbum!=null)
            {
                if (!previousAlbum.Equals("") && !album.Equals("Unknown Album") && previousAlbum.Equals(album)) return;    
            }
            else
            {
                previousAlbum = album;
            }

            try
            {
                if (String.IsNullOrEmpty(base64))
                {
                    cover = string.Empty;
                    return;
                }
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(base64)))
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
                        cover = Convert.ToBase64String(ms2.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError(ex);
                cover = String.Empty;
            }
            finally
            {
                previousAlbum = album;
                EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingCover, Constants.Message, cover).toJsonString()));
            }
        }

        public string Cover
        {
            get { return cover; }
        }

        public string Lyrics
        {
            set
            {
                try
                {
                    string lStr = value.Trim();
                    if (lStr.Contains("\r\r\n\r\r\n"))
                    {
                        /* Convert new line & empty line to xml safe format */
                        lStr = lStr.Replace("\r\r\n\r\r\n", " \r\n ");
                        lStr = lStr.Replace("\r\r\n", " \n ");
                    }
                    lStr = lStr.Replace("\0", " ");
                    //lStr = lStr.Replace("\r\n", "&lt;p&gt;");
                    //lStr = lStr.Replace("\n", "&lt;br&gt;");
                    const string pattern = "\\[\\d:\\d{2}.\\d{3}\\] ";
                    Regex regEx = new Regex(pattern);
                    lyrics = SecurityElement.Escape(regEx.Replace(lStr, String.Empty));
                }
                catch (Exception ex)
                {
#if DEBUG
                    ErrorHandler.LogError(ex);
#endif
                    lyrics = String.Empty;
                }
                finally
                {
                    if (!String.IsNullOrEmpty(lyrics))
                        EventBus.FireEvent(
                            new MessageEvent(EventType.ReplyAvailable,
                                new SocketMessage(Constants.NowPlayingLyrics, Constants.Message, lyrics).toJsonString()));
                }
            }
            get { return lyrics; }
        }
    }
}