using System;
using MusicBeePlugin.AndroidRemote.Enumerations;

namespace MusicBeePlugin.AndroidRemote.Events
{
    class ClientRequestArgs:EventArgs
    {
        private readonly RequestType _type;
        private readonly int _clientId;
        private readonly string _requestData;
        
        public ClientRequestArgs(RequestType type)
        {
            _type = type;
            _requestData = String.Empty;
            _clientId = -1;
        }

        public ClientRequestArgs(RequestType type, int clientId)
        {
            _type = type;
            _clientId = clientId;
            _requestData = String.Empty;
        }

        public ClientRequestArgs(RequestType type, int clientId, string requestData)
        {
            _type = type;
            _clientId = clientId;
            _requestData = requestData;
        }

        public RequestType Type
        {
            get { return _type; }
        }

        public string RequestData
        {
            get { return _requestData; }
        }

        public int ClientId
        {
            get { return _clientId; }
        }
    }
}
