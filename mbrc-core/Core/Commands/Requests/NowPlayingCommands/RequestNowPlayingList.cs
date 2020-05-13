using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.NowPlayingCommands
{
    public class RequestNowPlayingList : ICommand
    {
        private readonly Authenticator _auth;
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _nowPlayingApiAdapter;

        public RequestNowPlayingList(
            Authenticator auth,
            ITinyMessengerHub hub,
            INowPlayingApiAdapter nowPlayingApiAdapter)
        {
            _auth = auth;
            _hub = hub;
            _nowPlayingApiAdapter = nowPlayingApiAdapter;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var socketClient = _auth.GetConnection(receivedEvent.ConnectionId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            if (clientProtocol < 2.2 || !(receivedEvent.Data is JObject data))
            {
                var tracks = _nowPlayingApiAdapter.GetTracksLegacy().ToList();
                var message = new SocketMessage(Constants.NowPlayingList, tracks);
                _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
            }
            else
            {
                var offset = (int)data["offset"];
                var limit = (int)data["limit"];
                var tracks = _nowPlayingApiAdapter.GetTracks(offset, limit).ToList();
                var total = tracks.Count;
                var realLimit = offset + limit > total ? total - offset : limit;
                var message = new SocketMessage
                {
                    Context = Constants.NowPlayingList,
                    Data = new Page<Model.Entities.NowPlayingTrackInfo>
                    {
                        Data = offset > total ? new List<Model.Entities.NowPlayingTrackInfo>() : tracks.GetRange(offset, realLimit),
                        Offset = offset,
                        Limit = limit,
                        Total = total,
                    },
                    NewLineTerminated = true,
                };

                _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
            }
        }
    }
}
