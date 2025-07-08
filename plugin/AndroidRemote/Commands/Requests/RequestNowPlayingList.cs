using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingList : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestNowPlayingList(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            var socketClient = Authenticator.Client(eEvent.ClientId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            var data = eEvent.Data as JsonObject;
            if (clientProtocol < 2.2 || data == null)
            {
                _nowPlayingService.GetNowPlayingList(eEvent.ClientId);
            }
            else
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");

                if (socketClient?.ClientPlatform == ClientOS.Android)
                    _nowPlayingService.GetNowPlayingListPage(eEvent.ClientId, offset, limit);
                else
                    _nowPlayingService.GetNowPlayingListOrdered(eEvent.ClientId, offset, limit);
            }
        }
    }
}