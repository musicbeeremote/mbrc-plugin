using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibGenreArtists : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibGenreArtists(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            _libraryService.LibraryGetGenreArtists(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}