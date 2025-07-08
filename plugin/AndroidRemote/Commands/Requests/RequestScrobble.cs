using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestScrobble : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestScrobble(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            _playerService.SetScrobblerState(eEvent.Data.Equals("toggle")
                ? StateAction.Toggle
                : StateAction.State);
        }
    }
}