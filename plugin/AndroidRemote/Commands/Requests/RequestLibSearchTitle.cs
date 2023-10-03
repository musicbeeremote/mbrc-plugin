using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchTitle : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchTitle(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}