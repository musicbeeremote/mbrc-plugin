using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingTrackRemoval : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            if (int.TryParse(eEvent.DataToString(), out var index))
                Plugin.Instance.NowPlayingListRemoveTrack(index, eEvent.ClientId);
        }
    }
}