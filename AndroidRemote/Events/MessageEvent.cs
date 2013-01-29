namespace MusicBeePlugin.AndroidRemote.Events
{
    using System;
    using Interfaces;

    class MessageEvent:IEvent
    {
        private readonly string type;
        private readonly string data;
        private readonly string clientId;
        private readonly string extraData;

        public MessageEvent(string type, string data)
        {
            this.data = data;
            this.type = type;
            clientId = "all";
            extraData = String.Empty;
        }

        public MessageEvent(string type, string data, string clientId)
        {
            this.type = type;
            this.data = data;
            this.clientId = clientId;
            extraData = String.Empty;
        }

        public MessageEvent(string type)
        {
            this.type = type;
            data = extraData = String.Empty;
            clientId = string.Empty;
        }

        public MessageEvent(string type, string data, string clientId, string extraData)
        {
            this.type = type;
            this.data = data;
            this.clientId = clientId;
            this.extraData = extraData;
        }

        public string Data
        {
            get { return data; }
        }

        public string Type
        {
            get { return type; }
        }

        public string ClientId
        {
            get { return clientId; }
        }

        public string ExtraData
        {
            get
            {
                return extraData;
            }
        }
    }
}
