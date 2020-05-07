using System;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Handshake
{
    internal class RequestProtocol : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly Authenticator _auth;

        public RequestProtocol(ITinyMessengerHub hub, Authenticator auth)
        {
            _hub = hub;
            _auth = auth;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            if (receivedEvent.Data is JToken data)
            {
                int clientProtocolVersion;
                switch (data.Type)
                {
                    case JTokenType.Integer:
                    case JTokenType.Float:
                        clientProtocolVersion = (int)data;
                        break;
                    case JTokenType.Object:
                        clientProtocolVersion = (int)data["protocol_version"];
                        break;
                    default:
                        clientProtocolVersion = Constants.V2;
                        break;
                }

                var socketConnection = _auth.GetConnection(receivedEvent.ConnectionId);
                if (socketConnection != null)
                {
                    socketConnection.ClientProtocolVersion = clientProtocolVersion;
                }
            }

            var message = new SocketMessage(Constants.Protocol, Constants.ProtocolVersion);
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }
    }
}
