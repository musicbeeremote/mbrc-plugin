using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Model.Generators;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.InstaReplies
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
        private readonly IPlayerApiAdapter _apiAdapter;
        private readonly ITrackApiAdapter _trackApiAdapter;
        private readonly Authenticator _auth;

        public ProcessInitRequest(LyricCoverModel model,
            ITinyMessengerHub hub,
            IPlayerApiAdapter apiAdapter,
            ITrackApiAdapter trackApiAdapter,
            Authenticator auth)
        {
            _model = model;
            _hub = hub;
            _apiAdapter = apiAdapter;
            _trackApiAdapter = trackApiAdapter;
            _auth = auth;
        }

        public void Execute(IEvent @event)
        {
            var connectionId = @event.ConnectionId;

            var clientProtocol = _auth.ClientProtocolVersion(connectionId);

            SendTrackInfo(clientProtocol, connectionId);
            SendTrackRating(connectionId);
            SendLfmRating(connectionId);

            var statusMessage = new SocketMessage(Constants.PlayerStatus, _apiAdapter.GetStatus());
            _hub.Publish(new PluginResponseAvailableEvent(statusMessage, connectionId));


            if (clientProtocol >= Constants.V3)
            {
                var coverPayload = CoverPayloadGenerator.Create(_model.Cover, true);
                var lyricsPayload = new LyricsPayload(_model.Lyrics);
                var coverMessage = new SocketMessage(Constants.NowPlayingCover, coverPayload);
                var lyricsMessage = new SocketMessage(Constants.NowPlayingLyrics, lyricsPayload);
                _hub.Publish(new PluginResponseAvailableEvent(coverMessage, connectionId));
                _hub.Publish(new PluginResponseAvailableEvent(lyricsMessage, connectionId));
            }
            else
            {
                var coverMessage = new SocketMessage(Constants.NowPlayingCover, _model.Cover);
                var lyricsMessage = new SocketMessage(Constants.NowPlayingLyrics, _model.Lyrics);
                _hub.Publish(new PluginResponseAvailableEvent(coverMessage, connectionId));
                _hub.Publish(new PluginResponseAvailableEvent(lyricsMessage, connectionId));
            }
        }

        private void SendLfmRating(string connectionId)
        {
            var rating = _trackApiAdapter.GetLfmStatus();
            var message = new SocketMessage(Constants.NowPlayingLfmRating, rating);
            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }

        private void SendTrackRating(string connectionId)
        {
            var rating = _trackApiAdapter.GetRating();
            var message = new SocketMessage(Constants.NowPlayingRating, rating);
            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }

        private void SendTrackInfo(int clientProtocol, string connectionId)
        {
            NowPlayingTrackBase trackInfo;
            if (clientProtocol >= Constants.V3)
            {
                trackInfo = _trackApiAdapter.GetPlayingTrackInfo();
            }
            else
            {
                trackInfo = _trackApiAdapter.GetPlayingTrackInfoLegacy();
            }

            var trackInfoMessage = new SocketMessage(Constants.NowPlayingTrack, trackInfo);
            _hub.Publish(new PluginResponseAvailableEvent(trackInfoMessage, connectionId));
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
                var coverPayload = CoverPayloadGenerator.Create(_model.Cover, true);
                message = new SocketMessage(Constants.NowPlayingCover, coverPayload){NewLineTerminated = true};
            }
            else
            {
                message = new SocketMessage(Constants.NowPlayingCover, _model.Cover) {NewLineTerminated = true};
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

    internal class RequestCurrentPosition : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestCurrentPosition(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var temporalInformation = _apiAdapter.GetTemporalInformation();
            var message = new SocketMessage(Constants.NowPlayingPosition, temporalInformation);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestDetails : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        public RequestDetails(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var trackDetails = _apiAdapter.GetPlayingTrackDetails();
            var message = new SocketMessage(Constants.NowPlayingDetails, trackDetails);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }
}