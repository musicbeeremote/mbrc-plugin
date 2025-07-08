using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingSearch : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestNowPlayingSearch(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            _nowPlayingService.NowPlayingSearch(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}