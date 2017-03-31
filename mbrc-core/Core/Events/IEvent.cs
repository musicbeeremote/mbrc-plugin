using TinyMessenger;

namespace MusicBeeRemote.Core.Events
{
    public interface IEvent : ITinyMessage
    {
        object Data { get; }
        string Type { get; }
        string ConnectionId { get; }
        string ClientId { get; }
        string DataToString();
    }
}
