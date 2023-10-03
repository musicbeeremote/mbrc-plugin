using System;
using System.Security;
using System.Text.RegularExpressions;
using NLog;

namespace MusicBeePlugin.AndroidRemote.Model
{
    internal class LyricCoverModel
    {
        /** Singleton **/
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private string _lyrics;


        private string _xHash;

        private LyricCoverModel()
        {
        }

        public static LyricCoverModel Instance { get; } = new LyricCoverModel();

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
                    //lStr = lStr.Replace("\r\n", "&lt;p&gt;");
                    //lStr = lStr.Replace("\n", "&lt;br&gt;");
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
                    Plugin.BroadcastLyrics(_lyrics);
                }
            }
            get => _lyrics;
        }

        public void SetCover(string base64)
        {
            var hash = Utilities.Utilities.Sha1Hash(base64);

            if (_xHash != null && _xHash.Equals(hash)) return;

            Cover = string.IsNullOrEmpty(base64)
                ? string.Empty
                : Utilities.Utilities.ImageResize(base64);
            _xHash = hash;

            Plugin.BroadcastCover(Cover);
        }
    }
}