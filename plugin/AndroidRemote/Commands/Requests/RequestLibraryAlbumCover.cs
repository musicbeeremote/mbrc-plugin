using System;
using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestLibraryAlbumCover : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var data = eEvent.Data as JsonObject;
            var album = data.Get<string>("album");
            var artist = data.Get<string>("artist") ?? string.Empty;
            var hash = data.Get<string>("hash") ?? string.Empty;
            var size = data.Get<string>("size");
            Plugin.Instance.RequestCover(eEvent.ClientId, artist, album, hash, size);
        }
    }
}
