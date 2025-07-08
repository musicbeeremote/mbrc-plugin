using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingPlay : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestNowPlayingPlay(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            var socketClient = Authenticator.Client(eEvent.ClientId);
            var isAndroid = socketClient?.ClientPlatform == ClientOS.Android;
            _nowPlayingService.NowPlayingPlay(eEvent.DataToString(), isAndroid);
        }
    }
}