using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Playlists
{
    internal class RequestPlaylistPlay : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ILibraryApiAdapter _libraryApiAdapter;

        public RequestPlaylistPlay(ITinyMessengerHub hub, ILibraryApiAdapter libraryApiAdapter)
        {
            _hub = hub;
            _libraryApiAdapter = libraryApiAdapter;
        }

        public override string Name()
        {
            return "Playlist: Play";
        }

        public override void Execute(IEvent receivedEvent)
        {
            var success = false;
            var token = receivedEvent.DataToken();
            if (token != null && token.Type == JTokenType.String)
            {
                var url = token.Value<string>();
                success = _libraryApiAdapter.PlayPlaylist(url);
            }

            var message = new SocketMessage(Constants.PlaylistPlay, success);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.AddTrack;
        }
    }
}
