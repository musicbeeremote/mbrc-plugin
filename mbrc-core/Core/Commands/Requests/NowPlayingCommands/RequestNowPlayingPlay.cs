using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.NowPlaying
{
    public class RequestNowPlayingPlay : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _nowPlayingApiAdapter;

        public RequestNowPlayingPlay(ITinyMessengerHub hub, INowPlayingApiAdapter nowPlayingApiAdapter)
        {
            _hub = hub;
            _nowPlayingApiAdapter = nowPlayingApiAdapter;
        }

        /// <inheritdoc />
        public override string Name() => "Now Playing: Play";

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var result = false;

            if (receivedEvent.Data is JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Integer:
                        result = _nowPlayingApiAdapter.PlayIndex((int)token);
                        break;
                    case JTokenType.String:
                        result = _nowPlayingApiAdapter.PlayPath((string)token);
                        break;
                }
            }

            var message = new SocketMessage(Constants.NowPlayingListPlay, result);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.StartPlayback;
        }
    }
}
