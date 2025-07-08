using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    internal class ProcessInitRequest : ICommand
    {
        private readonly IPlayerService _playerService;
        private readonly INowPlayingService _nowPlayingService;

        public ProcessInitRequest(IPlayerService playerService, INowPlayingService nowPlayingService)
        {
            _playerService = playerService;
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            _nowPlayingService.RequestTrackInfo(eEvent.ClientId);
            _nowPlayingService.RequestTrackRating("-1", eEvent.ClientId);
            _playerService.RequestLoveStatus(eEvent.DataToString(), eEvent.ClientId);
            _playerService.RequestPlayerStatus(eEvent.ClientId);
            Plugin.BroadcastCover(LyricCoverModel.Instance.Cover);
            _nowPlayingService.RequestNowPlayingTrackLyrics();
        }
    }
}