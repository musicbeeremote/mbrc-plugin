using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerState
{
    public class RequestNextTrack : LimitedCommand
    {
        private readonly IPlayerApiAdapter _apiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestNextTrack(IPlayerApiAdapter apiAdapter, ITinyMessengerHub hub)
        {
            _apiAdapter = apiAdapter;
            _hub = hub;
        }

        public override string Name()
        {
            return "Player: Play next";
        }

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var message = new SocketMessage(Constants.PlayerNext, _apiAdapter.PlayNext());
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.PlayNext;
        }
    }
}
