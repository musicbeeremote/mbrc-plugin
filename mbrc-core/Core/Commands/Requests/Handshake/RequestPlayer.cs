using System;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Handshake
{
    public class RequestPlayer : ICommand
    {
        private readonly ITinyMessengerHub _hub;

        public RequestPlayer(ITinyMessengerHub hub)
        {
            _hub = hub;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var message = new SocketMessage(Constants.Player, "MusicBee");
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }
    }
}
