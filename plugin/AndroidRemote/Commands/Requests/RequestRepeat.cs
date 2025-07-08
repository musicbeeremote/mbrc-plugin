using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestRepeat : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestRepeat(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.SetRepeatState(eEvent.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }
    }
}