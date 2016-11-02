using MusicBeePlugin.AndroidRemote.Utilities;
using static MusicBeePlugin.AndroidRemote.Networking.Constants;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    using Entities;
    using Interfaces;
    using Model;
    using Networking;

    internal class RequestCover : ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            SocketMessage message;

            if (Authenticator.ClientProtocolVersion(eEvent.ClientId) > 2)
            {
                var coverPayload = new CoverPayload(LyricCoverModel.Instance.Cover, true);
                message = new SocketMessage(NowPlayingCover, coverPayload);
            }
            else
            {
                message = new SocketMessage(NowPlayingCover, LyricCoverModel.Instance.Cover);
            }
            SocketServer.Instance.Send(message.ToJsonString(), eEvent.ClientId);
        }
    }
}