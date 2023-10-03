using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestSongDetails : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestTrackDetails(eEvent.ClientId);
        }
    }
}