using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestProtocol : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            int clientProtocolVersion;
            if (int.TryParse(eEvent.DataToString(), out clientProtocolVersion))
            {
                var connection = Authenticator.GetConnection(eEvent.ConnectionId);
                if (connection != null)
                {
                    connection.ClientProtocolVersion = clientProtocolVersion;
                }
            }
            SocketServer.Instance.Send(new SocketMessage(Constants.Protocol, Constants.ProtocolVersion).ToJsonString(),
                eEvent.ConnectionId);
        }
    }

    internal class RequestPlayer : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(new SocketMessage(Constants.Player, "MusicBee").ToJsonString(),
                eEvent.ConnectionId);
        }
    }
}