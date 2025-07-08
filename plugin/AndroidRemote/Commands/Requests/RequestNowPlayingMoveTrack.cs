using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingMoveTrack : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestNowPlayingMoveTrack(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            ((Dictionary<string, string>)eEvent.Data).TryGetValue("from", out var sFrom);
            ((Dictionary<string, string>)eEvent.Data).TryGetValue("to", out var sTo);
            int.TryParse(sFrom, out var from);
            int.TryParse(sTo, out var to);
            _nowPlayingService.RequestNowPlayingMove(eEvent.ClientId, from, to);
        }
    }
}