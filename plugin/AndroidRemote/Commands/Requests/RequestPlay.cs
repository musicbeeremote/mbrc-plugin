using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlay : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlay(eEvent.ClientId);
        }
    }
}