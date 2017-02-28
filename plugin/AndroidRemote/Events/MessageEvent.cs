using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Events
{
    internal class MessageEvent : IEvent
    {
        public MessageEvent(string type, object data)
        {
            Data = data;
            Type = type;
            ClientId = "all";
            ExtraData = string.Empty;
        }

        public MessageEvent(string type, object data, string clientId)
        {
            Type = type;
            Data = data;
            ClientId = clientId;
            ExtraData = string.Empty;
        }

        public MessageEvent(string type)
        {
            Type = type;
            Data = ExtraData = string.Empty;
            ClientId = string.Empty;
        }

        public MessageEvent(string type, object data, string clientId, string extraData)
        {
            Type = type;
            Data = data;
            ClientId = clientId;
            ExtraData = extraData;
        }

        public string DataToString()
        {
            return (string) (Data is string ? Data : string.Empty);
        }

        public object Data { get; }

        public string Type { get; }

        public string ClientId { get; }

        public string ExtraData { get; }
    }
}