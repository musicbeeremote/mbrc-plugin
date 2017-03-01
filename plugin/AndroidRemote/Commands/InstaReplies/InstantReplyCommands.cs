using System;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using NLog;

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
        public void Execute(IEvent eEvent)
        {
            var message = new SocketMessage(Constants.Pong, string.Empty)
                .ToJsonString();
            SocketServer.Instance.Send(message);
        }
    }

    internal class ProcessInitRequest : ICommand

    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestTrackInfo(eEvent.ConnectionId);
            Plugin.Instance.RequestTrackRating("-1", eEvent.ConnectionId);
            Plugin.Instance.RequestLoveStatus(eEvent.DataToString(), eEvent.ConnectionId);
            Plugin.Instance.RequestPlayerStatus(eEvent.ConnectionId);
            Plugin.BroadcastCover(LyricCoverModel.Instance.Cover);
            Plugin.BroadcastLyrics(LyricCoverModel.Instance.Lyrics);
        }
    }

    internal class RequestCover : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketMessage message;

            if (Authenticator.ClientProtocolVersion(eEvent.ConnectionId) > 2)
            {
                var coverPayload = new CoverPayload(LyricCoverModel.Instance.Cover, true);
                message = new SocketMessage(Constants.NowPlayingCover, coverPayload);
            }
            else
            {
                message = new SocketMessage(Constants.NowPlayingCover, LyricCoverModel.Instance.Cover);
            }
            SocketServer.Instance.Send(message.ToJsonString(), eEvent.ConnectionId);
        }
    }

    internal class RequestLyrics : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            if (Authenticator.ClientProtocolVersion(eEvent.ConnectionId) > 2)
            {
                var lyricsPayload = new LyricsPayload(LyricCoverModel.Instance.Lyrics);
                SocketServer.Instance.Send(new SocketMessage(Constants.NowPlayingLyrics, lyricsPayload).ToJsonString(),
                    eEvent.ConnectionId);
            }
            else
            {
                SocketServer.Instance.Send(
                    new SocketMessage(Constants.NowPlayingLyrics, LyricCoverModel.Instance.Lyrics).ToJsonString(),
                    eEvent.ConnectionId);
            }
        }
    }
}