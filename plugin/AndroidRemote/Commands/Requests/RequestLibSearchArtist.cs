using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchArtist : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchArtist(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}