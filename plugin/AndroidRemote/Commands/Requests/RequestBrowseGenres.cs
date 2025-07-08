using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestBrowseGenres : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestBrowseGenres(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            if (eEvent.Data is JsonObject data)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                _libraryService.BrowseGenres(eEvent.ClientId, offset, limit);
            }
            else
            {
                _libraryService.BrowseGenres(eEvent.ClientId);
            }
        }
    }
}