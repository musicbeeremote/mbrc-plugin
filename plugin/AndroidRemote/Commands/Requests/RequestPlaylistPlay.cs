using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestPlaylistPlay : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.PlayPlaylist(eEvent.ClientId, eEvent.DataToString());
        }
    }
}