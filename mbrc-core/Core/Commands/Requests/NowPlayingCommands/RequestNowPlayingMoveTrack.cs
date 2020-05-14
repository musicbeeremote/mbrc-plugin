using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.NowPlayingCommands
{
    public class RequestNowPlayingMoveTrack : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _nowPlayingApiAdapter;

        public RequestNowPlayingMoveTrack(ITinyMessengerHub hub, INowPlayingApiAdapter nowPlayingApiAdapter)
        {
            _hub = hub;
            _nowPlayingApiAdapter = nowPlayingApiAdapter;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var from = -1;
            var to = -1;

            var success = false;

            if (receivedEvent.Data is JToken token && token.Type == JTokenType.Object)
            {
                from = (int)token["from"];
                to = (int)token["to"];
                success = _nowPlayingApiAdapter.MoveTrack(from, to);
            }

            var reply = new { success, from, to };

            var message = new SocketMessage(Constants.NowPlayingListMove, reply);
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }
    }
}
