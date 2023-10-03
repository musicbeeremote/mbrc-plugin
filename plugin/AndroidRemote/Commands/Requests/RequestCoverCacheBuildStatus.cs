using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestCoverCacheBuildStatus : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.BroadcastCoverCacheBuildStatus(eEvent.ClientId);
        }
    }
}