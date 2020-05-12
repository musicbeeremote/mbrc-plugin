using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.NowPlaying
{
    internal class RequestNowPlayingTrackRemoval : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _nowPlayingApiAdapter;

        public RequestNowPlayingTrackRemoval(ITinyMessengerHub hub, INowPlayingApiAdapter nowPlayingApiAdapter)
        {
            _hub = hub;
            _nowPlayingApiAdapter = nowPlayingApiAdapter;
        }

        /// <inheritdoc />
        public override string Name()
        {
            return "Now Playing: Remove Track";
        }

        public override void Execute(IEvent receivedEvent)
        {
            var success = false;
            var token = receivedEvent.Data as JToken;
            var index = -1;

            if (token != null && token.Type == JTokenType.Integer)
            {
                index = (int)token;
                success = _nowPlayingApiAdapter.RemoveIndex(index);
            }

            var reply = new { success, index };

            var message = new SocketMessage(Constants.NowPlayingListRemove, reply);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.RemoveTrack;
        }
    }
}
