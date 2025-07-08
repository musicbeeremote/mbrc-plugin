using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibArtistAlbums : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibArtistAlbums(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            _libraryService.LibraryGetArtistAlbums(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}