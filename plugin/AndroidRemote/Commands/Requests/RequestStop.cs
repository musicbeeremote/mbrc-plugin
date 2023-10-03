using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestStop : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestStopPlayback(eEvent.ClientId);
        }
    }
}