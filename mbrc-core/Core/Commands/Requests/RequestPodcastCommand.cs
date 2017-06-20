using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Podcasts;
using Newtonsoft.Json.Linq;
using TinyMessenger;
using static MusicBeeRemote.Core.Commands.Requests.PaginatedData;

namespace MusicBeeRemote.Core.Commands.Requests
{
    internal class RequestPodcastCommand : PaginatedDataCommand<PodcastSubscription>
    {
        private readonly ILibraryApiAdapter _libraryApiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPodcastCommand(ILibraryApiAdapter libraryApiAdapter, ITinyMessengerHub hub)
        {
            _libraryApiAdapter = libraryApiAdapter;
            _hub = hub;
        }

        protected override List<PodcastSubscription> GetData()
        {
            return _libraryApiAdapter.GetPodcastSubscriptions().ToList();
        }

        protected override string Context() => Constants.PodcastSubscriptions;

        internal override void Publish(PluginResponseAvailableEvent @event)
        {
            _hub.Publish(@event);
        }
    }

    internal class RequestPodcastEpisodeCommand : ICommand
    {
        private readonly ILibraryApiAdapter _libraryApiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPodcastEpisodeCommand(ILibraryApiAdapter libraryApiAdapter, ITinyMessengerHub hub)
        {
            _libraryApiAdapter = libraryApiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            var data = @event.Data as JObject;
            if (data != null)
            {
                var offset = (int) data["offset"];
                var limit = (int) data["limit"];
                var id = (string) data["id"];
                GetPage(@event.ConnectionId, id, offset, limit);
            }
            else
            {
                GetPage(@event.ConnectionId);
            }
        }


        private void GetPage(string connectionId, string id = "", int offset = 0, int limit = 800)
        {
            var data = _libraryApiAdapter.GetEpisodes(id).ToList();
            var message = CreateMessage(offset, limit, data, Constants.PodcastEpisodes);
            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }

    internal class RequestPodcastArtworkCommand : ICommand
    {
        private readonly ILibraryApiAdapter _libraryApiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPodcastArtworkCommand(ILibraryApiAdapter libraryApiAdapter, ITinyMessengerHub hub)
        {
            _libraryApiAdapter = libraryApiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            var data = @event.Data as JObject;
            var connectionId = @event.ConnectionId;
            if (data == null)
            {
                Publish(Message((int) HttpStatusCode.BadRequest, "no artwork"), connectionId);
                return;
            }
            var id = (string) data["id"];
            if (string.IsNullOrWhiteSpace(id))
            {
                Publish(Message((int) HttpStatusCode.BadRequest, "missing id"), connectionId);
                return;
            }
            var artwork = _libraryApiAdapter.GetPodcastSubscriptionArtwork(id);
            var base64 = Convert.ToBase64String(artwork);
            
            Publish(Message((int) HttpStatusCode.OK, "", base64), connectionId);                       
        }

        private SocketMessage Message(int code, string description, string artwork = null)
        {
            return new SocketMessage(Constants.PodcastArtwork, new ArtworkResponse
            {
                Code = code,
                Description = description,
                Artwork = artwork
            });
        }

        private void Publish(SocketMessage message, string connectionId)
        {
            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}