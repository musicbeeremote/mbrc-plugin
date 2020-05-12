using TinyMessenger;

namespace MusicBeeRemote.Core.Model
{
    public class LyricsDataReadyEvent : ITinyMessage
    {
        public LyricsDataReadyEvent(string lyrics)
        {
            Lyrics = lyrics;
        }

        public string Lyrics { get; }

        public object Sender { get; } = null;
    }
}
