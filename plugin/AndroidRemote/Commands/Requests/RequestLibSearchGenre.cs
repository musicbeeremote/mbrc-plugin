using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchGenre : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibSearchGenre(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            _libraryService.LibrarySearchGenres(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}