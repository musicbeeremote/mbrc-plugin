using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Events
{
    internal class MessageEvent : IEvent
    {
        public MessageEvent(string type, object data, string connectionId, string clientId)
        {
            Type = type;
            Data = data;
            ConnectionId = connectionId;
            ClientId = clientId;
        }

        public string DataToString()
        {
            return (string) (Data is string ? Data : string.Empty);
        }

        public object Data { get; }

        public string Type { get; }

        public string ConnectionId { get; }

        public string ClientId { get; }

        public object Sender { get; } = null;
    }
}