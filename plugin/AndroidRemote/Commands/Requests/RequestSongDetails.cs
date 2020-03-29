using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestSongDetails : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestTrackDetails(eEvent.ClientId);
        }
    }
}
