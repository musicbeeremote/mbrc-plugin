using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Utilities;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
{
    internal class RequestPlaylistPlay : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.PlayPlaylist(@event.ConnectionId, @event.DataToString());
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.AddTrack;
    }

    internal class RequestPlaylistList : ICommand
    {
        private readonly Authenticator _auth;

        public RequestPlaylistList(Authenticator auth)
        {
            _auth = auth;
        }

        public void Execute(IEvent @event)
        {
            var socketClient = _auth.GetConnection(@event.ConnectionId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            var data = @event.Data as JsonObject;
            if (clientProtocol < 2.2 || data == null)
            {
                Plugin.Instance.GetAvailablePlaylistUrls(@event.ConnectionId);
            }
            else
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");

                Plugin.Instance.GetAvailablePlaylistUrls(@event.ConnectionId, offset, limit);
            }
        }
    }
}