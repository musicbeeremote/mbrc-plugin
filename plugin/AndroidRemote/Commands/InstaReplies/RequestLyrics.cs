using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    using Model;
    using Interfaces;
    using Networking;

    internal class RequestLyrics : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            if (Authenticator.ClientProtocolVersion(eEvent.ClientId) > 2)
            {
                var lyricsPayload = new LyricsPayload(LyricCoverModel.Instance.Lyrics);
                SocketServer.Instance.Send(new SocketMessage(Constants.NowPlayingLyrics, lyricsPayload).ToJsonString(), eEvent.ClientId);
            }
            else
            {
                SocketServer.Instance.Send(new SocketMessage(Constants.NowPlayingLyrics, LyricCoverModel.Instance.Lyrics).ToJsonString(), eEvent.ClientId);
            }

        }
    }
}