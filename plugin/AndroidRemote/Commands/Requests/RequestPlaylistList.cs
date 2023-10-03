using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestPlaylistList : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var socketClient = Authenticator.Client(eEvent.ClientId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            if (clientProtocol < 2.2 || !(eEvent.Data is JsonObject data))
            {
                Plugin.Instance.GetAvailablePlaylistUrls(eEvent.ClientId);
            }
            else
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");

                Plugin.Instance.GetAvailablePlaylistUrls(eEvent.ClientId, offset, limit);
            }
        }
    }
}