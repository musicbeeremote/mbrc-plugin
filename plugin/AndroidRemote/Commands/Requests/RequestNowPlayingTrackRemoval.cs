using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingTrackRemoval : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestNowPlayingTrackRemoval(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            if (int.TryParse(eEvent.DataToString(), out var index))
                _nowPlayingService.NowPlayingListRemoveTrack(index, eEvent.ClientId);
        }
    }
}