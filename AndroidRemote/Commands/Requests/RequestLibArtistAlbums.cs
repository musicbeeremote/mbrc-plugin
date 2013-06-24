using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestLibArtistAlbums : ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibraryGetArtistAlbums(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}
