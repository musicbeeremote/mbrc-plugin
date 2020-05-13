using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerState
{
    public class RequestPlayPause : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPlayPause(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        /// <inheritdoc />
        public override string Name()
        {
            return "Player: Play/Pause";
        }

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var message = new SocketMessage(Constants.PlayerPause, _apiAdapter.PlayPause());
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.StartPlayback |
                   CommandPermissions.StopPlayback;
        }
    }
}
