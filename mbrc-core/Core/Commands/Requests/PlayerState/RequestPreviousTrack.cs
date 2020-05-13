using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerState
{
    public class RequestPreviousTrack : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPreviousTrack(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override string Name()
        {
            return "Player: Play previous";
        }

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var message = new SocketMessage(Constants.PlayerPrevious, _apiAdapter.PlayPrevious());
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.PlayPrevious;
        }
    }
}
