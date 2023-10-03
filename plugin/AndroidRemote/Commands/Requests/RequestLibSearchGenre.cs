using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchGenre : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchGenres(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}