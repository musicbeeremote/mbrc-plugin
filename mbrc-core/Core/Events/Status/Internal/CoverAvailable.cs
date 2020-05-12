using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Status.Internal
{
    internal class CoverAvailable : ITinyMessage
    {
        public CoverAvailable(string cover)
        {
            Cover = cover;
        }

        public object Sender { get; } = null;

        public string Cover { get; }
    }
}
