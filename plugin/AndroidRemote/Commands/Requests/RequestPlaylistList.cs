using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestPlaylistList : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.GetAvailablePlaylistUrls(eEvent.ClientId);
        }
    }
}