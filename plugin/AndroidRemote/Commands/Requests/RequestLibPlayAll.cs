using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;
using ServiceStack;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibPlayAll : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibPlayAll(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            _libraryService.LibraryPlayAll(eEvent.ClientId, eEvent.Data.ConvertTo<bool>());
        }
    }
}