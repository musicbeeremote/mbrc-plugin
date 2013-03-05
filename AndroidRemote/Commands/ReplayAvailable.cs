using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    class ReplayAvailable : ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(eEvent.NullTerminatedXmlString, eEvent.ClientId);
        }
    }
}
