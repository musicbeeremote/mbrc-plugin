using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    [DataContract]
    public class LyricsPayload
    {
        public LyricsPayload(string lyrics)
        {
            Lyrics = lyrics;
            Status = string.IsNullOrEmpty(lyrics) ? NotFound : Success;
        }

        public const int Retrieving = 1;
        public const int NotFound = 404;
        public const int Success = 200;

        [DataMember(Name = "status")]
        public int Status { get; set; }

        [DataMember(Name = "lyrics")]
        public string Lyrics { get; set; }
    }
}