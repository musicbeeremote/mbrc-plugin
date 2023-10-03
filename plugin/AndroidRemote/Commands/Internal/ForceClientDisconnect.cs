using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class ForceClientDisconnect : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.KickClient(eEvent.ClientId);
        }
    }
}