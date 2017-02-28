namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;

    internal class RequestLibSearchGenre : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchGenres(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}
