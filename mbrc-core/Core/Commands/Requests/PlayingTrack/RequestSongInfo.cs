using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.PlayingTrack
{
    public class RequestSongInfo : ICommand
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

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var connectionId = receivedEvent.ConnectionId;

            var protocolVersion = _auth.ClientProtocolVersion(connectionId);
            var message = protocolVersion > 2
                ? new SocketMessage(Constants.NowPlayingTrack, _apiAdapter.GetPlayingTrackInfo())
                : new SocketMessage(Constants.NowPlayingTrack, _apiAdapter.GetPlayingTrackInfoLegacy());

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}
