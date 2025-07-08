using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlayerStatus : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestPlayerStatus(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.GetPlayerStatus(eEvent.ClientId);
        }
    }
}