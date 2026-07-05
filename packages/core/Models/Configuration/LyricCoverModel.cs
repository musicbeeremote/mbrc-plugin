using System;
using System.Security;
using System.Text.RegularExpressions;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Services.Core;

namespace MusicBeePlugin.Models.Configuration
{
    public class LyricCoverModel
    {
        private readonly IBroadcaster _broadcaster;
        private readonly IPluginLogger _logger;

        private string _lyrics;
        private string _xHash;

        public LyricCoverModel(IPluginLogger logger, IBroadcaster broadcaster)
        {
            _logger = logger;
            _broadcaster = broadcaster;
        }

        public virtual string Cover { get; private set; }

        public virtual string Lyrics
        {
            set
            {
                try
                {
                    var lStr = value.Trim();
                    if (lStr.IndexOf("\r\r\n\r\r\n", StringComparison.Ordinal) >= 0)
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
                    _logger.LogError(ex, "Lyrics processing");
                    _lyrics = string.Empty;
                }
                finally
                {
                    _broadcaster.BroadcastLyrics(_lyrics);
                }
            }
            get => _lyrics;
        }

        public virtual void SetCover(string base64)
        {
            var hash = Utilities.Common.Utilities.Sha1Hash(base64);

            if (_xHash != null && _xHash.Equals(hash, StringComparison.Ordinal))
                return;

            Cover = string.IsNullOrEmpty(base64)
                ? string.Empty
                : Utilities.Common.Utilities.ImageResize(base64);
            _xHash = hash;

            _broadcaster.BroadcastCover(Cover);
        }
    }
}
