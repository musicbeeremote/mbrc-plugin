using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPreviousTrack : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestPreviousTrack(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.PreviousTrack(eEvent.ClientId);
        }
    }
}