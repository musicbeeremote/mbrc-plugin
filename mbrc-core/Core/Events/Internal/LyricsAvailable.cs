using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Internal
{
    internal class LyricsAvailable : ITinyMessage
    {
        public LyricsAvailable(string lyrics)
        {
            Lyrics = lyrics;
        }

        public object Sender { get; } = null;

        public string Lyrics { get; }
    }
}
