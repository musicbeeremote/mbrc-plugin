using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class RestartSocketCommand : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.RestartSocket();
        }
    }
}