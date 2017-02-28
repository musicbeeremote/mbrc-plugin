namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;

    internal class RequestLibSearchArtist:ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchArtist(eEvent.Data.ToString(), eEvent.ClientId);    
        }
    }
}
