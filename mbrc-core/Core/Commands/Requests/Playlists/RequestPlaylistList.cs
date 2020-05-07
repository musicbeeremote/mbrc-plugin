using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Playlists
{
    internal class RequestPlaylistList : ICommand
    {
        private readonly Authenticator _auth;
        private readonly ILibraryApiAdapter _libraryApiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPlaylistList(Authenticator auth, ILibraryApiAdapter libraryApiAdapter, ITinyMessengerHub hub)
        {
            _auth = auth;
            _libraryApiAdapter = libraryApiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent receivedEvent)
        {
            var socketClient = _auth.GetConnection(receivedEvent.ConnectionId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            if (clientProtocol < 2.2 || !(receivedEvent.Data is JObject data))
            {
                SendPage(receivedEvent.ConnectionId);
            }
            else
            {
                var offset = (int)data["offset"];
                var limit = (int)data["limit"];
                SendPage(receivedEvent.ConnectionId, offset, limit);
            }
        }

        private void SendPage(string connectionId, int offset = 0, int limit = 500)
        {
            var playlists = _libraryApiAdapter.GetPlaylists().ToList();
            var total = playlists.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.PlaylistList,
                Data = new Page<Playlist>
                {
                    Data = offset > total ? new List<Playlist>() : playlists.GetRange(offset, realLimit),
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
