using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestRepeat : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestRepeatState(eEvent.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }
    }
}