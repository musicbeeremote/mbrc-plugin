using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestBrowseArtists : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            if (data != null)
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