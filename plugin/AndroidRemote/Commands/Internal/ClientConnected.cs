using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class ClientConnected : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Authenticator.AddClientOnConnect(eEvent.ClientId);
        }
    }
}