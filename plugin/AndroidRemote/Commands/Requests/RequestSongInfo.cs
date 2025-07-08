using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestSongInfo : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestSongInfo(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            _nowPlayingService.GetTrackInfo(eEvent.ClientId);
        }
    }
}