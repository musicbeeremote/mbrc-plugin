using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerState
{
    public class RequestAutoDj : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestAutoDj(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        /// <inheritdoc />
        public override string Name()
        {
            return "Player: AutoDJ";
        }

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var isToggle = receivedEvent.Data is JToken token &&
                           ((string)token).Equals("toggle", StringComparison.InvariantCultureIgnoreCase);

            if (isToggle)
            {
                _apiAdapter.ToggleAutoDjLegacy();
            }

            var message = new SocketMessage(Constants.PlayerAutoDj, _apiAdapter.IsAutoDjEnabledLegacy());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.ChangeShuffle;
        }
    }
}
