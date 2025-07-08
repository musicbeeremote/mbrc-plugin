using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNextTrack : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestNextTrack(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.NextTrack(eEvent.ClientId);
        }
    }
}