using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestVolume : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestVolume(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            if (!int.TryParse(eEvent.DataToString(), out var iVolume)) return;

            _playerService.SetVolume(iVolume);
        }
    }
}