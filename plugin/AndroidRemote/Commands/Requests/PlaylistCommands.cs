using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlaylistPlay : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.PlayPlaylist(eEvent.ConnectionId, eEvent.DataToString());
        }
    }

    internal class RequestPlaylistList : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var socketClient = Authenticator.GetConnection(eEvent.ConnectionId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            var data = eEvent.Data as JsonObject;
            if (clientProtocol < 2.2 || data == null)
            {
                Plugin.Instance.GetAvailablePlaylistUrls(eEvent.ConnectionId);
            }
            else
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");

                Plugin.Instance.GetAvailablePlaylistUrls(eEvent.ConnectionId, offset, limit);
            }

        }
    }
}