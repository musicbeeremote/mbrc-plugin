using System;
using System.Net;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Podcasts;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Podcasts
{
    public class RequestPodcastArtworkCommand : ICommand
    {
        private readonly ILibraryApiAdapter _libraryApiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPodcastArtworkCommand(ILibraryApiAdapter libraryApiAdapter, ITinyMessengerHub hub)
        {
            _libraryApiAdapter = libraryApiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            var connectionId = receivedEvent.ConnectionId;

            if (!(receivedEvent.Data is JObject data))
            {
                Publish(Message((int)HttpStatusCode.BadRequest, "no artwork"), connectionId);
                return;
            }

            var id = (string)data["id"];
            if (string.IsNullOrWhiteSpace(id))
            {
                Publish(Message((int)HttpStatusCode.BadRequest, "missing id"), connectionId);
                return;
            }

            var artwork = _libraryApiAdapter.GetPodcastSubscriptionArtwork(id);
            var base64 = Convert.ToBase64String(artwork);

            Publish(Message((int)HttpStatusCode.OK, string.Empty, base64), connectionId);
        }

        private static SocketMessage Message(int code, string description, string artwork = null)
        {
            return new SocketMessage(
                Constants.PodcastArtwork,
                new ArtworkResponse { Code = code, Description = description, Artwork = artwork });
        }

        private void Publish(SocketMessage message, string connectionId)
        {
            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}
