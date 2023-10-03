using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNextTrack : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestNextTrack(eEvent.ClientId);
        }
    }
}