using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlayPause : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestPlayPause(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.PlayPauseTrack(eEvent.ClientId);
        }
    }
}