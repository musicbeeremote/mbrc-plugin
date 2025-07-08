using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestMute : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestMute(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.ToggleMute(eEvent.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }
    }
}