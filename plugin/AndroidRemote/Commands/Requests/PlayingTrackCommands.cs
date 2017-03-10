using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLfmLoveRating : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestLoveStatus(@event.DataToString(), @event.ConnectionId);
        }
    }

    public class RequestPlaybackPosition : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestPlayPosition(@event.DataToString());
        }
    }

    internal class RequestRating : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestTrackRating(@event.DataToString(), @event.ConnectionId);
        }
    }

    internal class RequestSongInfo : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestTrackInfo(@event.ConnectionId);
        }
    }
}