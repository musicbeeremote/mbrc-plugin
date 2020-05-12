using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Status.Internal
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
