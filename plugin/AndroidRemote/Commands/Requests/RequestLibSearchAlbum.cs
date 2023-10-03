using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchAlbum : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchAlbums(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}