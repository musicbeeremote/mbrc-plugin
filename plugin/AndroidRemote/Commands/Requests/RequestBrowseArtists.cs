using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestBrowseArtists : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestBrowseArtists(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            if (eEvent.Data is JsonObject data)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                var type = data.Get<bool>("album_artists");
                _libraryService.BrowseArtists(eEvent.ClientId, offset, limit, type);
            }
            else
            {
                _libraryService.BrowseArtists(eEvent.ClientId);
            }
        }
    }
}