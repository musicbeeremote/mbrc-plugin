using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibArtistAlbums : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibraryGetArtistAlbums(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}