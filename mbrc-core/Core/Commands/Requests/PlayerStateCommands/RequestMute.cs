using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerStateCommands
{
    public class RequestMute : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestMute(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override string Name()
        {
            return "Player: Mute";
        }

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            bool isToggle = receivedEvent.Data is JToken token && ((string)token).Equals("toggle", StringComparison.InvariantCultureIgnoreCase);

            if (isToggle)
            {
                _apiAdapter.ToggleMute();
            }

            var message = new SocketMessage(Constants.PlayerMute, _apiAdapter.IsMuted());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.CanMute;
        }
    }
}
