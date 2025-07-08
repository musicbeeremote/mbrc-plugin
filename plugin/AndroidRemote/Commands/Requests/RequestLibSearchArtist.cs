using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchArtist : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibSearchArtist(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            _libraryService.LibrarySearchArtist(eEvent.Data.ToString(), eEvent.ClientId);
        }
    }
}