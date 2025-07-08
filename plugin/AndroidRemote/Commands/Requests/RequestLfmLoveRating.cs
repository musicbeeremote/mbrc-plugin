using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLfmLoveRating : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestLfmLoveRating(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.RequestLoveStatus(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}