using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPause : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPausePlayback(eEvent.ClientId);
        }
    }
}
