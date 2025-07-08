using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlaylistPlay : ICommand
    {
        private readonly IPlaylistService _playlistService;

        public RequestPlaylistPlay(IPlaylistService playlistService)
        {
            _playlistService = playlistService;
        }

        public void Execute(IEvent eEvent)
        {
            _playlistService.PlayPlaylist(eEvent.ClientId, eEvent.DataToString());
        }
    }
}