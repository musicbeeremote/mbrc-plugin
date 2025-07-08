using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestPlayerOutputSwitch : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestPlayerOutputSwitch(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.SwitchOutputDevice(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}