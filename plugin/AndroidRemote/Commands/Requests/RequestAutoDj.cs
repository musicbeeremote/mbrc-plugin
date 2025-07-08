using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestAutoDj : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestAutoDj(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.SetAutoDjState((string)eEvent.Data == "toggle"
                ? StateAction.Toggle
                : StateAction.State);
        }
    }
}