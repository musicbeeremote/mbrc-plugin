using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerState
{
    internal class RequestRepeat : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestRepeat(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override string Name()
        {
            return "Player: Change Repeat";
        }

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent.Data is JToken token &&
                ((string)token).Equals("toggle", StringComparison.InvariantCultureIgnoreCase))
            {
                _apiAdapter.ToggleRepeatMode();
            }

            var message = new SocketMessage(Constants.PlayerRepeat, _apiAdapter.GetRepeatMode());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.ChangeRepeat;
        }
    }
}
