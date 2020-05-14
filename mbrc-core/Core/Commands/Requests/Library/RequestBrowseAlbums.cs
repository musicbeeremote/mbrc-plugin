using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Library
{
    public class RequestBrowseAlbums : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ILibraryApiAdapter _apiAdapter;

        public RequestBrowseAlbums(ITinyMessengerHub hub, ILibraryApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            if (receivedEvent.Data is JObject data)
            {
                var offset = (int)data["offset"];
                var limit = (int)data["limit"];
                SendPage(receivedEvent.ConnectionId, offset, limit);
            }
            else
            {
                SendPage(receivedEvent.ConnectionId);
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
                    Total = total,
                },
                NewLineTerminated = true,
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}
