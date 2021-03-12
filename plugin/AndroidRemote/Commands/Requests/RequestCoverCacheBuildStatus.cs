namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;

    internal class RequestCoverCacheBuildStatus : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.BroadcastCoverCacheBuildStatus(eEvent.ClientId);
        }
    }
}