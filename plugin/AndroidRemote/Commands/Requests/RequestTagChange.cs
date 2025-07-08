using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestTagChange : ICommand
    {
        private readonly IPlayerService _playerService;

        public RequestTagChange(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            var tagName = data.Get<string>("tag");
            var newValue = data.Get<string>("value");

            _playerService.SetTrackTag(tagName, newValue, eEvent.ClientId);
        }
    }
}