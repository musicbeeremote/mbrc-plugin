using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlayerStatus : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayerStatus(eEvent.ClientId);
        }
    }
}