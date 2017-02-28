using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class ClientDisconnected:ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            Authenticator.RemoveClientOnDisconnect(eEvent.ClientId);
        }
    }
}
