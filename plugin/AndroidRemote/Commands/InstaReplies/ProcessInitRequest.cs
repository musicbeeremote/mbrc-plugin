using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;

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
            Plugin.BroadcastCover(LyricCoverModel.Instance.Cover);
            Plugin.BroadcastLyrics(LyricCoverModel.Instance.Lyrics);
        }
    }
}