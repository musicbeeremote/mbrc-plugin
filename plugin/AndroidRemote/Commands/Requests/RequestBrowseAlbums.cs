using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestBrowseAlbums : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseAlbums(eEvent.ClientId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseAlbums(eEvent.ClientId);
            }
        }
    }
}