using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestSongDetails : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestSongDetails(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            _nowPlayingService.GetTrackDetails(eEvent.ClientId);
        }
    }
}