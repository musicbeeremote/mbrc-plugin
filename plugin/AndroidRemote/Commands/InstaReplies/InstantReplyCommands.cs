using System;
using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using NLog;
using TinyMessenger;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    internal class HandlePong : ICommand
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public void Execute(IEvent @event)
        {
            _logger.Debug($"Pong: {DateTime.UtcNow}");
        }
    }

    public class PingReply : ICommand
    {
        private readonly ITinyMessengerHub _hub;

        public PingReply(ITinyMessengerHub hub)
        {
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.Pong, string.Empty);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class ProcessInitRequest : ICommand
    {
        private readonly LyricCoverModel _model;
        private readonly ITinyMessengerHub _hub;
        private readonly Authenticator _auth;

        public ProcessInitRequest(LyricCoverModel model, ITinyMessengerHub hub)
        {
            _model = model;
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestTrackInfo(@event.ConnectionId);
            Plugin.Instance.RequestTrackRating("-1", @event.ConnectionId);
            Plugin.Instance.RequestLoveStatus(@event.DataToString(), @event.ConnectionId);
            Plugin.Instance.RequestPlayerStatus(@event.ConnectionId);
            var clientProtocol = _auth.ClientProtocolVersion(@event.ConnectionId);

            if (clientProtocol >= Constants.V3)
            {
                var coverPayload = new CoverPayload(_model.Cover, true);
                var lyricsPayload = new LyricsPayload(_model.Lyrics);
                var coverMessage = new SocketMessage(Constants.NowPlayingCover, coverPayload);
                var lyricsMessage = new SocketMessage(Constants.NowPlayingLyrics, lyricsPayload);
                _hub.Publish(new PluginResponseAvailableEvent(coverMessage, @event.ConnectionId));
                _hub.Publish(new PluginResponseAvailableEvent(lyricsMessage, @event.ConnectionId));
            }
            else
            {
                var coverMessage = new SocketMessage(Constants.NowPlayingCover, _model.Cover);
                var lyricsMessage = new SocketMessage(Constants.NowPlayingLyrics, _model.Lyrics);
                _hub.Publish(new PluginResponseAvailableEvent(coverMessage, @event.ConnectionId));
                _hub.Publish(new PluginResponseAvailableEvent(lyricsMessage, @event.ConnectionId));
            }
        }
    }

    internal class RequestCover : ICommand
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

        public void Execute(IEvent @event)
        {
            SocketMessage message;

            if (_auth.ClientProtocolVersion(@event.ConnectionId) > 2)
            {
                var coverPayload = new CoverPayload(_model.Cover, true);
                message = new SocketMessage(Constants.NowPlayingCover, coverPayload);
            }
            else
            {
                message = new SocketMessage(Constants.NowPlayingCover, _model.Cover);
            }
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

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

        public void Execute(IEvent @event)
        {
            if (_auth.ClientProtocolVersion(@event.ConnectionId) > 2)
            {
                var lyricsPayload = new LyricsPayload(_model.Lyrics);
                var message = new SocketMessage(Constants.NowPlayingLyrics, lyricsPayload);
                _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
            }
            else
            {
                var message = new SocketMessage(Constants.NowPlayingLyrics, _model.Lyrics);
                _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
            }
        }
    }
}