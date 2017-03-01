using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestBrowseTracks : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseTracks(eEvent.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseTracks(eEvent.ConnectionId);
            }
        }
    }

    public class RequestBrowseGenres : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseGenres(eEvent.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseGenres(eEvent.ConnectionId);
            }
        }
    }

    public class RequestBrowseArtists : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseArtists(eEvent.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseArtists(eEvent.ConnectionId);
            }
        }
    }

    public class RequestBrowseAlbums : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseAlbums(eEvent.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseAlbums(eEvent.ConnectionId);
            }
        }
    }
}