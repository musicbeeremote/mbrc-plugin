using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    internal class RequestLyrics : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestLyrics(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            _nowPlayingService.RequestNowPlayingTrackLyrics();
        }
    }
}