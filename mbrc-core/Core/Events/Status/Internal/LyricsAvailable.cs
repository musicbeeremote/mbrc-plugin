using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Status.Internal
{
    public class LyricsAvailable : ITinyMessage
    {
        public LyricsAvailable(string lyrics)
        {
            Lyrics = lyrics;
        }

        public object Sender { get; } = null;

        public string Lyrics { get; }
    }
}
