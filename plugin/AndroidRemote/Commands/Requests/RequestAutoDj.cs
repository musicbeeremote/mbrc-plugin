using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestAutoDj : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestAutoDjState((string)eEvent.Data == "toggle"
                ? StateAction.Toggle
                : StateAction.State);
        }
    }
}