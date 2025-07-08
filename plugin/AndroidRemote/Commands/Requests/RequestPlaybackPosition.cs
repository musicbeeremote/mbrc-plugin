using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestPlaybackPosition : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestPlaybackPosition(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.SetPlayPosition(eEvent.DataToString());
        }
    }
}