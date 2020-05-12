using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.InstantReplies
{
    internal class RequestLyrics : ICommand
    {
        private readonly LyricCoverModel _model;
        private readonly ITinyMessengerHub _hub;
        private readonly Authenticator _auth;

        public RequestLyrics(LyricCoverModel model, ITinyMessengerHub hub, Authenticator auth)
        {
            _model = model;
            _hub = hub;
            _auth = auth;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (_auth.ClientProtocolVersion(receivedEvent.ConnectionId) > 2)
            {
                var lyricsPayload = new LyricsPayload(_model.Lyrics);
                var message = new SocketMessage(Constants.NowPlayingLyrics, lyricsPayload);
                _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
            }
            else
            {
                var message = new SocketMessage(Constants.NowPlayingLyrics, _model.Lyrics);
                _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
            }
        }
    }
}
