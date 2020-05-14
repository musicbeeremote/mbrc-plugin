using System;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Model.Generators;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.InstantReplies
{
    public class RequestCover : ICommand
    {
        private readonly LyricCoverModel _model;
        private readonly ITinyMessengerHub _hub;
        private readonly Authenticator _auth;

        public RequestCover(LyricCoverModel model, ITinyMessengerHub hub, Authenticator auth)
        {
            _model = model;
            _hub = hub;
            _auth = auth;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            SocketMessage message;

            if (_auth.ClientProtocolVersion(receivedEvent.ConnectionId) > 2)
            {
                var coverPayload = CoverPayloadGenerator.Create(_model.Cover, true);
                message = new SocketMessage(Constants.NowPlayingCover, coverPayload) { NewLineTerminated = true };
            }
            else
            {
                message = new SocketMessage(Constants.NowPlayingCover, _model.Cover) { NewLineTerminated = true };
            }

            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }
    }
}
