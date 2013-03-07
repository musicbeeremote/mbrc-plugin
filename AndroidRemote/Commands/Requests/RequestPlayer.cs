namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Entities;
    using Interfaces;
    using Networking;

    class RequestPlayer:ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(new SocketMessage(Constants.Player, Constants.Reply, "MusicBee").toJsonString(), eEvent.ClientId);
        }
    }
}
