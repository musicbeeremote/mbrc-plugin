using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class InitializeModelStateCommand : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public InitializeModelStateCommand(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            _nowPlayingService.RequestNowPlayingTrackCover();
            _nowPlayingService.RequestNowPlayingTrackLyrics();
        }
    }
}