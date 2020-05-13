using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.InstantReplies
{
    public class RequestCurrentPosition : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestCurrentPosition(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var temporalInformation = _apiAdapter.GetTemporalInformation();
            var message = new SocketMessage(Constants.NowPlayingPosition, temporalInformation);
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }
    }
}
