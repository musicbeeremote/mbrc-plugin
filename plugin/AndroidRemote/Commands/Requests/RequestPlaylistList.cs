using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestPlaylistList : ICommand
    {
        private readonly IPlaylistService _playlistService;

        public RequestPlaylistList(IPlaylistService playlistService)
        {
            _playlistService = playlistService;
        }

        public void Execute(IEvent eEvent)
        {
            var socketClient = Authenticator.Client(eEvent.ClientId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            if (clientProtocol < 2.2 || !(eEvent.Data is JsonObject data))
            {
                _playlistService.GetAvailablePlaylistUrls(eEvent.ClientId);
            }
            else
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");

                _playlistService.GetAvailablePlaylistUrls(eEvent.ClientId, offset, limit);
            }
        }
    }
}