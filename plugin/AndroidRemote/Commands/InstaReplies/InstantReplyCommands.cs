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

        public void Execute(IEvent eEvent)
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

        public void Execute(IEvent eEvent)
        {
            var message = new SocketMessage(Constants.Pong, string.Empty);
            _hub.Publish(new PluginResponseAvailableEvent(message, eEvent.ConnectionId));
        }
    }

    internal class ProcessInitRequest : ICommand
    {
        private readonly LyricCoverModel _model;
        private readonly ITinyMessengerHub _hub;

        public ProcessInitRequest(LyricCoverModel model, ITinyMessengerHub hub)
        {
            _model = model;
            _hub = hub;
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestTrackInfo(eEvent.ConnectionId);
            Plugin.Instance.RequestTrackRating("-1", eEvent.ConnectionId);
            Plugin.Instance.RequestLoveStatus(eEvent.DataToString(), eEvent.ConnectionId);
            Plugin.Instance.RequestPlayerStatus(eEvent.ConnectionId);
            //Plugin.BroadcastCover(LyricCoverModel.Instance.Cover);
            //Plugin.BroadcastLyrics(LyricCoverModel.Instance.Lyrics);
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

        public void Execute(IEvent eEvent)
        {
            SocketMessage message;

            if (_auth.ClientProtocolVersion(eEvent.ConnectionId) > 2)
            {
                var coverPayload = new CoverPayload(_model.Cover, true);
                message = new SocketMessage(Constants.NowPlayingCover, coverPayload);
            }
            else
            {
                message = new SocketMessage(Constants.NowPlayingCover, _model.Cover);
            }
            _hub.Publish(new PluginResponseAvailableEvent(message, eEvent.ConnectionId));
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

        public void Execute(IEvent eEvent)
        {
            if (_auth.ClientProtocolVersion(eEvent.ConnectionId) > 2)
            {
                var lyricsPayload = new LyricsPayload(_model.Lyrics);
                var message = new SocketMessage(Constants.NowPlayingLyrics, lyricsPayload);
                _hub.Publish(new PluginResponseAvailableEvent(message, eEvent.ConnectionId));
            }
            else
            {
                var message = new SocketMessage(Constants.NowPlayingLyrics, _model.Lyrics);
                _hub.Publish(new PluginResponseAvailableEvent(message, eEvent.ConnectionId));
            }
        }
    }
}