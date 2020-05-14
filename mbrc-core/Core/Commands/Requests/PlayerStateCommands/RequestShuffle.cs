using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayerStateCommands
{
    public class RequestShuffle : LimitedCommand
    {
        private readonly Authenticator _auth;
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestShuffle(Authenticator auth, ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _auth = auth;
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        /// <inheritdoc />
        public override string Name()
        {
            return "Player: Change shuffle";
        }

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var isToggle = receivedEvent.Data is JToken token &&
                           ((string)token).Equals("toggle", StringComparison.InvariantCultureIgnoreCase);

            SocketMessage message;
            if (_auth.ClientProtocolMisMatch(receivedEvent.ConnectionId))
            {
                if (isToggle)
                {
                    _apiAdapter.ToggleShuffleLegacy();
                }

                message = new SocketMessage(Constants.PlayerShuffle, _apiAdapter.GetShuffleLegacy());
            }
            else
            {
                var shuffleState = isToggle ? _apiAdapter.SwitchShuffle() : _apiAdapter.GetShuffleState();
                message = new SocketMessage(Constants.PlayerShuffle, shuffleState);
            }

            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.ChangeShuffle;
        }
    }
}
