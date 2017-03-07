using NLog;
using TinyMessenger;

namespace MusicBeePlugin.AndroidRemote.Model
{
    using System;
    using System.Security;
    using System.Text.RegularExpressions;

    internal class LyricCoverModel
    {
        private readonly ITinyMessengerHub _messengerHub;

        /** Singleton **/
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private string _xHash;
        private string _lyrics;

        public LyricCoverModel(ITinyMessengerHub messengerHub)
        {
            _messengerHub = messengerHub;
        }

        public void SetCover(string base64)
        {
            var hash = Utilities.Utilities.Sha1Hash(base64);

            if (_xHash != null && _xHash.Equals(hash))
            {
                return;
            }

            Cover = string.IsNullOrEmpty(base64)
                ? string.Empty
                : Utilities.Utilities.ImageResize(base64);
            _xHash = hash;

            _messengerHub.Publish(new CoverDataReadyEvent(Cover));
        }

        public string Cover { get; private set; }

        public string Lyrics
        {
            set
            {
                try
                {
                    var lStr = value.Trim();
                    if (lStr.Contains("\r\r\n\r\r\n"))
                    {
                        /* Convert new line & empty line to xml safe format */
                        lStr = lStr.Replace("\r\r\n\r\r\n", " \r\n ");
                        lStr = lStr.Replace("\r\r\n", " \n ");
                    }
                    lStr = lStr.Replace("\0", " ");
                    const string pattern = "\\[\\d:\\d{2}.\\d{3}\\] ";
                    var regEx = new Regex(pattern);
                    _lyrics = SecurityElement.Escape(regEx.Replace(lStr, string.Empty));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Lyrics processing");
                    _lyrics = string.Empty;
                }
                finally
                {
                    _messengerHub.Publish(new LyricsDataReadyEvent(_lyrics));
                }
            }
            get { return _lyrics; }
        }
    }

    internal class LyricsDataReadyEvent : ITinyMessage
    {
        public string Lyrics { get; }

        public LyricsDataReadyEvent(string lyrics)
        {
            Lyrics = lyrics;
        }

        public object Sender { get; } = null;
    }

    internal class CoverDataReadyEvent : ITinyMessage
    {
        public string Cover { get; }

        public CoverDataReadyEvent(string cover)
        {
            Cover = cover;
        }

        public object Sender { get; } = null;
    }
}