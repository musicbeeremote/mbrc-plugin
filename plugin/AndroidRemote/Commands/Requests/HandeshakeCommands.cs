using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using TinyMessenger;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestProtocol : ICommand
    {
        private readonly ITinyMessengerHub _messengerHub;
        private readonly Authenticator _auth;

        public RequestProtocol(ITinyMessengerHub messengerHub, Authenticator auth)
        {
            _messengerHub = messengerHub;
            _auth = auth;
        }

        public void Execute(IEvent eEvent)
        {
            int clientProtocolVersion;
            if (int.TryParse(eEvent.DataToString(), out clientProtocolVersion))
            {
                var connection = _auth.GetConnection(eEvent.ConnectionId);
                if (connection != null)
                {
                    connection.ClientProtocolVersion = clientProtocolVersion;
                }
            }
            var message = new SocketMessage(Constants.Protocol, Constants.ProtocolVersion);
            _messengerHub.Publish(new PluginResponseAvailableEvent(message, eEvent.ConnectionId));
        }
    }

    internal class RequestPlayer : ICommand
    {
        private readonly ITinyMessengerHub _messengerHub;

        public RequestPlayer(ITinyMessengerHub messengerHub)
        {
            _messengerHub = messengerHub;
        }

        public void Execute(IEvent eEvent)
        {
            var message = new SocketMessage(Constants.Player, "MusicBee");
            _messengerHub.Publish(new PluginResponseAvailableEvent(message, eEvent.ConnectionId));
        }
    }
}