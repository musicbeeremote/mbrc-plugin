using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    internal class ProcessInitRequest : ICommand

    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestTrackInfo(eEvent.ClientId);
            Plugin.Instance.RequestTrackRating("-1", eEvent.ClientId);
            Plugin.Instance.RequestLoveStatus(eEvent.DataToString(), eEvent.ClientId);
            Plugin.Instance.RequestPlayerStatus(eEvent.ClientId);
            

            var cover =
                new SocketMessage(Constants.NowPlayingCover, LyricCoverModel.Instance.Cover)
                    .ToJsonString();
            SocketServer.Instance.Send(cover, eEvent.ClientId);
            SocketServer.Instance.Send(new SocketMessage(Constants.NowPlayingLyrics, LyricCoverModel.Instance.Lyrics).ToJsonString(), eEvent.ClientId);
        }
    }
}