using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    internal class ReplayAvailable : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(eEvent.Data + "\r\n", eEvent.ClientId);
        }
    }
}