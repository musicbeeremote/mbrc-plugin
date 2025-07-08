using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestShuffle : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestShuffle(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            var stateAction = eEvent.Data.Equals("toggle")
                ? StateAction.Toggle
                : StateAction.State;

            if (Authenticator.ClientProtocolMisMatch(eEvent.ClientId))
                _playerService.SetShuffleState(stateAction);
            else
                _playerService.SetAutoDjShuffleState(stateAction);
        }
    }
}