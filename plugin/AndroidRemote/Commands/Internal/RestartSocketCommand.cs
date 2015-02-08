using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    class RestartSocketCommand:ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.RestartSocket();
        }

        public void Dispose()
        {
        }
    }
}
