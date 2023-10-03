using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestScrobble : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestScrobblerState(eEvent.Data.Equals("toggle")
                ? StateAction.Toggle
                : StateAction.State);
        }
    }
}