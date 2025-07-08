using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestRating : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestRating(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            _nowPlayingService.SetTrackRating(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}