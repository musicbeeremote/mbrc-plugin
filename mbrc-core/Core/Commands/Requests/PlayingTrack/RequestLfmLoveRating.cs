using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayingTrack
{
    internal class RequestLfmLoveRating : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestLfmLoveRating(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent receivedEvent)
        {
            var token = receivedEvent.DataToken();
            LastfmStatus lfmStatus;
            if (token != null && token.Type == JTokenType.String)
            {
                var action = token.Value<string>();
                lfmStatus = _apiAdapter.ChangeStatus(action);
            }
            else
            {
                lfmStatus = _apiAdapter.GetLfmStatus();
            }

            var message = new SocketMessage(Constants.NowPlayingLfmRating, lfmStatus);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }
    }
}
