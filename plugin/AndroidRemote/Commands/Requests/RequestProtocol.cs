
namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Entities;
    using Interfaces;
    using Networking;

    class RequestProtocol:ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(new SocketMessage(Constants.Protocol, Constants.Reply, Constants.ProtocolVersion).toJsonString(), eEvent.ClientId); 
        }
    }
}
