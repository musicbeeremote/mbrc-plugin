using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using static MusicBeePlugin.AndroidRemote.Networking.Constants;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    internal class RequestCover : ICommand
    {
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