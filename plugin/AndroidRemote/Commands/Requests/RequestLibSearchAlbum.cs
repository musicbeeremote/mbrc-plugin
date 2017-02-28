namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;

    internal class RequestLibSearchAlbum : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchAlbums(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}
