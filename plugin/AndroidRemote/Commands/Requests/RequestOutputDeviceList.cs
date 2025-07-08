using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestOutputDeviceList : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestOutputDeviceList(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.RequestOutputDevice(eEvent.ClientId);
        }
    }
}