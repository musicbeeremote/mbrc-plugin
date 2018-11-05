using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
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

        public void Execute(IEvent @event)
        {
            var token = @event.DataToken();
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

    public class RequestPlaybackPosition : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestPlaybackPosition(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var token = @event.DataToken();
            if (token != null && token.Type == JTokenType.Integer)
            {
                var position = token.Value<int>();
                _apiAdapter.SeekTo(position);
            }
            
            var message = new SocketMessage(Constants.NowPlayingPosition, _apiAdapter.GetTemporalInformation());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }
    }

    internal class RequestRating : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestRating(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            string result;
            var token = @event.DataToken();
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

    internal class RequestSongInfo : ICommand
    {
        private readonly Authenticator _auth;
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestSongInfo(Authenticator auth, ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
        {
            _auth = auth;
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var connectionId = @event.ConnectionId;

            var protocolVersion = _auth.ClientProtocolVersion(connectionId);
            var message = protocolVersion > 2
                ? new SocketMessage(Constants.NowPlayingTrack, _apiAdapter.GetPlayingTrackInfo())
                : new SocketMessage(Constants.NowPlayingTrack, _apiAdapter.GetPlayingTrackInfoLegacy());

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}