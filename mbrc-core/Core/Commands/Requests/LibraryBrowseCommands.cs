using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
{
    public class RequestBrowseTracks : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ILibraryApiAdapter _adapter;

        public RequestBrowseTracks(ILibraryApiAdapter adapter, ITinyMessengerHub hub)
        {
            _adapter = adapter;
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            var data = @event.Data as JObject;
            if (data != null)
            {
                var offset = (int) data["offset"];
                var limit = (int) data["limit"];
                SendPage(@event.ConnectionId, offset, limit);
            }
            else
            {
                SendPage(@event.ConnectionId);
            }
        }

        private void SendPage(string connectionId, int offset = 0, int limit = 4000)
        {
            var tracks = _adapter.GetTracks().ToList();
            var total = tracks.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseTracks,
                Data = new Page<Track>
                {
                    Data = offset > total ? new List<Track>() : tracks.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                },
                NewLineTerminated = true
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }

    public class RequestBrowseGenres : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ILibraryApiAdapter _adapter;

        public RequestBrowseGenres(ITinyMessengerHub hub, ILibraryApiAdapter adapter)
        {
            _hub = hub;
            _adapter = adapter;
        }

        public void Execute(IEvent @event)
        {
            var data = @event.Data as JObject;
            if (data != null)
            {
                var offset = (int) data["offset"];
                var limit = (int) data["limit"];
                SendPage(@event.ConnectionId, offset, limit);
            }
            else
            {
                SendPage(@event.ConnectionId);
            }
        }

        private void SendPage(string connectionId, int offset = 0, int limit = 4000)
        {
            var genres = _adapter.GetGenres().ToList();
            var total = genres.Count;
            var realLimit = offset + limit > total ? total - offset : limit;

            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseGenres,
                Data = new Page<Genre>
                {
                    Data = offset > total ? new List<Genre>() : genres.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                },
                NewLineTerminated = true
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }

    public class RequestBrowseArtists : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ILibraryApiAdapter _apiAdapter;

        public RequestBrowseArtists(ITinyMessengerHub hub, ILibraryApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var data = @event.Data as JObject;
            if (data != null)
            {
                var offset = (int) data["offset"];
                var limit = (int) data["limit"];
                SendPage(@event.ConnectionId, offset, limit);
            }
            else
            {
                SendPage(@event.ConnectionId);
            }
        }

        private void SendPage(string connectionId, int offset = 0, int limit = 4000)
        {
            var artists = _apiAdapter.GetArtists().ToList();
            var total = artists.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseArtists,
                Data = new Page<Artist>
                {
                    Data = offset > total ? new List<Artist>() : artists.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                },
                NewLineTerminated = true
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }

    public class RequestBrowseAlbums : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ILibraryApiAdapter _apiAdapter;

        public RequestBrowseAlbums(ITinyMessengerHub hub, ILibraryApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var data = @event.Data as JObject;
            if (data != null)
            {
                var offset = (int) data["offset"];
                var limit = (int) data["limit"];
                SendPage(@event.ConnectionId, offset, limit);
            }
            else
            {
                SendPage(@event.ConnectionId);
            }
        }

        private void SendPage(string connectionId, int offset = 0, int limit = 4000)
        {
            var albums = _apiAdapter.GetAlbums().ToList();
            var total = albums.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseAlbums,
                Data = new Page<Album>
                {
                    Data = offset > total ? new List<Album>() : albums.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                },
                NewLineTerminated = true
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}