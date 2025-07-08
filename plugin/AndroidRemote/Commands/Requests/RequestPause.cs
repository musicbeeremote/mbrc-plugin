using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPause : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestPause(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.Pause(eEvent.ClientId);
        }
    }
}