using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Model
{
    using System;
    using System.Security;
    using System.Text.RegularExpressions;
    using Error;
    using Events;

    internal class LyricCoverModel
    {
        /** Singleton **/
        private static readonly LyricCoverModel Model = new LyricCoverModel();

        private string xHash;
        private string cover;
        private string lyrics;

        public static LyricCoverModel Instance
        {
            get { return Model; }
        }

        private LyricCoverModel()
        {
            
        }

        public void SetCover(string base64)
        {
            var hash = Utilities.Utilities.Sha1Hash(base64);

            if (xHash != null && xHash.Equals(hash))
            {
                return;
            }

            cover = String.IsNullOrEmpty(base64)
                ? String.Empty
                : Utilities.Utilities.ImageResize(base64);
            xHash = hash;

            EventBus.FireEvent(
                    new MessageEvent(EventType.ReplyAvailable,
                        new SocketMessage(Constants.NowPlayingCover, Constants.Message, cover).toJsonString()));
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