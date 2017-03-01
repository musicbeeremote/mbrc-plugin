using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLfmLoveRating : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestLoveStatus(eEvent.DataToString(), eEvent.ConnectionId);
        }
    }

    public class RequestPlaybackPosition : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayPosition(eEvent.DataToString());
        }
    }

    internal class RequestRating : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestTrackRating(eEvent.DataToString(), eEvent.ConnectionId);
        }
    }

    internal class RequestSongInfo : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestTrackInfo(eEvent.ConnectionId);
        }
    }
}