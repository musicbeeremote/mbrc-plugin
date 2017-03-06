using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Events
{
    internal class MessageEvent : IEvent
    {
        public MessageEvent(string type, object data)
        {
            Data = data;
            Type = type;
            ConnectionId = "all";
            ExtraData = string.Empty;
        }

        public MessageEvent(string type, object data, string clientId)
        {
            Type = type;
            Data = data;
            ConnectionId = clientId;
            ExtraData = string.Empty;
        }

        public MessageEvent(string type)
        {
            Type = type;
            Data = ExtraData = string.Empty;
            ConnectionId = string.Empty;
        }

        public MessageEvent(string type, object data, string clientId, string extraData)
        {
            Type = type;
            Data = data;
            ConnectionId = clientId;
            ExtraData = extraData;
        }

        public string DataToString()
        {
            return (string) (Data is string ? Data : string.Empty);
        }

        public object Data { get; }

        public string Type { get; }

        public string ConnectionId { get; }

        public string ExtraData { get; }

        public object Sender { get; } = null;
    }
}