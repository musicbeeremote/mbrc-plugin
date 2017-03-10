using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestBrowseTracks : ICommand
    {
        public void Execute(IEvent @event)
        {
            var data = @event.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseTracks(@event.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseTracks(@event.ConnectionId);
            }
        }
    }

    public class RequestBrowseGenres : ICommand
    {
        public void Execute(IEvent @event)
        {
            var data = @event.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseGenres(@event.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseGenres(@event.ConnectionId);
            }
        }
    }

    public class RequestBrowseArtists : ICommand
    {
        public void Execute(IEvent @event)
        {
            var data = @event.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseArtists(@event.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseArtists(@event.ConnectionId);
            }
        }
    }

    public class RequestBrowseAlbums : ICommand
    {
        public void Execute(IEvent @event)
        {
            var data = @event.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.LibraryBrowseAlbums(@event.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.LibraryBrowseAlbums(@event.ConnectionId);
            }
        }
    }
}