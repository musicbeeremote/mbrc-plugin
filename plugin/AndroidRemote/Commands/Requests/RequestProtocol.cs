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
            if (int.TryParse(eEvent.DataToString(), out var clientProtocolVersion))
            {
                var client = Authenticator.Client(eEvent.ClientId);
                if (client != null) client.ClientProtocolVersion = clientProtocolVersion;
            }

            SocketServer.Instance.Send(new SocketMessage(Constants.Protocol, Constants.ProtocolVersion).ToJsonString(),
                eEvent.ClientId);
        }
    }
}