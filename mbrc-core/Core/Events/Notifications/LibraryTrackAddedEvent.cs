using TinyMessenger;

namespace MusicBeeRemote.Core.Events.Notifications
{
    class LibraryTrackAddedEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }
}
