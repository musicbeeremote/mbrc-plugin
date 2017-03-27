using MusicBeeRemoteCore.ApiAdapters;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
{
    internal class RequestLfmLoveRating : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestLoveStatus(@event.DataToString(), @event.ConnectionId);
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
            int position;
            if (int.TryParse(@event.DataToString(), out position))
            {
                _apiAdapter.SeekTo(position);
            }
            var message = new SocketMessage(Constants.NowPlayingPosition, _apiAdapter.GetTemporalInformation());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }
    }

    internal class RequestRating : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestTrackRating(@event.DataToString(), @event.ConnectionId);
        }
    }

    internal class RequestSongInfo : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestTrackInfo(@event.ConnectionId);
        }
    }
}