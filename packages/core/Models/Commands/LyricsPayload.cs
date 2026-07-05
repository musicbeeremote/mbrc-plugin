using System.Globalization;
using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Commands
{
    [DataContract]
    public class LyricsPayload
    {
        private const int NotFound = 404;
        private const int Success = 200;

        public LyricsPayload(string lyrics)
        {
            Lyrics = lyrics;
            Status = string.IsNullOrEmpty(lyrics) ? NotFound : Success;
        }

        [DataMember(Name = "status")] public int Status { get; set; }

        [DataMember(Name = "lyrics")] public string Lyrics { get; set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1}, {2}: {3}", nameof(Status), Status, nameof(Lyrics), Lyrics);
        }
    }
}
