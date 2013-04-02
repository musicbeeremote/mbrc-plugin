namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;
    class RequestLibSearchTitle : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchTitle(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}

