using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestBrowseArtists : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            if (eEvent.Data is JsonObject data)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                var type = data.Get<bool>("album_artists");
                Plugin.Instance.LibraryBrowseArtists(eEvent.ClientId, offset, limit, type);
            }
            else
            {
                Plugin.Instance.LibraryBrowseArtists(eEvent.ClientId);
            }
        }
    }
}