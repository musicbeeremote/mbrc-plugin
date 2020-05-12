using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayingTrack
{
    public class RequestPlaybackPosition : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestPlaybackPosition(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
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

            var token = receivedEvent.DataToken();
            if (token != null && token.Type == JTokenType.Integer)
            {
                var position = token.Value<int>();
                _apiAdapter.SeekTo(position);
            }

            var message = new SocketMessage(Constants.NowPlayingPosition, _apiAdapter.GetTemporalInformation());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }
    }
}
