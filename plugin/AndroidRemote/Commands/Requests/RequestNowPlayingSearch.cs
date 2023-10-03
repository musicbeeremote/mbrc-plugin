using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingSearch : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.NowPlayingSearch(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}