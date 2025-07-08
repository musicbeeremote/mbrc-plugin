using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibAlbumTracks : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibAlbumTracks(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            _libraryService.LibraryGetAlbumTracks(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}