using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
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

        public void Execute(IEvent @event)
        {
            var data = @event.Data as JToken;
            if (data != null)
            {
                int clientProtocolVersion;
                switch (data.Type)
                {
                    case JTokenType.Integer:
                    case JTokenType.Float:
                        clientProtocolVersion = (int) data;
                        break;
                    case JTokenType.Object:
                        clientProtocolVersion = (int) data["protocol_version"];
                        break;
                    default:
                        clientProtocolVersion = Constants.V2;
                        break;
                }

                var socketConnection = _auth.GetConnection(@event.ConnectionId);
                if (socketConnection != null)
                {
                    socketConnection.ClientProtocolVersion = clientProtocolVersion;
                }
            }


            var message = new SocketMessage(Constants.Protocol, Constants.ProtocolVersion);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestPlayer : ICommand
    {
        private readonly ITinyMessengerHub _hub;

        public RequestPlayer(ITinyMessengerHub hub)
        {
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.Player, "MusicBee");
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }
}