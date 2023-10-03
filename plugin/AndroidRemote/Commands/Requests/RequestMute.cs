using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestMute : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestMuteState(eEvent.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }
    }
}