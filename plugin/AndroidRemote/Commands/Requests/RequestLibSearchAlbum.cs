using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchAlbum : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibSearchAlbum(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            _libraryService.LibrarySearchAlbums(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}