using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestShuffle : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var stateAction = eEvent.Data.Equals("toggle")
                ? StateAction.Toggle
                : StateAction.State;

            if (Authenticator.ClientProtocolMisMatch(eEvent.ClientId))
                Plugin.Instance.RequestShuffleState(stateAction);
            else
                Plugin.Instance.RequestAutoDjShuffleState(stateAction);
        }
    }
}