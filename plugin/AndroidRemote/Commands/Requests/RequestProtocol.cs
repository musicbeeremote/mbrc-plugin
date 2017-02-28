using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestProtocol:ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            int clientProtocolVersion;
            if (int.TryParse(eEvent.DataToString(), out clientProtocolVersion))
            {
                var client = Authenticator.Client(eEvent.ClientId);
                if (client != null)
                {
                    client.ClientProtocolVersion = clientProtocolVersion;
                }
            }
            SocketServer.Instance.Send(new SocketMessage(Constants.Protocol, Constants.ProtocolVersion).ToJsonString(), eEvent.ClientId); 
        }
    }
}
