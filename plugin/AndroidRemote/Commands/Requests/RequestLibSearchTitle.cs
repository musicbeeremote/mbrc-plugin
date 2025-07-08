using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchTitle : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibSearchTitle(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            _libraryService.LibrarySearchTitle(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}