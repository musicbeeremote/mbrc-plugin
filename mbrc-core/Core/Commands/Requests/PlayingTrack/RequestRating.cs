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
    public class RequestRating : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestRating(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
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

            string result;
            var token = receivedEvent.DataToken();
            if (token != null)
            {
                var rating = token.Value<string>();
                result = _apiAdapter.SetRating(rating);
            }
            else
            {
                result = _apiAdapter.GetRating();
            }

            var message = new SocketMessage(Constants.NowPlayingRating, result);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }
    }
}
