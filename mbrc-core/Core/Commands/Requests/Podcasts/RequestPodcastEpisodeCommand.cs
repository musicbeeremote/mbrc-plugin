using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Podcasts
{
    internal class RequestPodcastEpisodeCommand : ICommand
    {
        private readonly ILibraryApiAdapter _libraryApiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPodcastEpisodeCommand(ILibraryApiAdapter libraryApiAdapter, ITinyMessengerHub hub)
        {
            _libraryApiAdapter = libraryApiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent receivedEvent)
        {
            var data = receivedEvent.Data as JObject;
            if (data != null)
            {
                var offset = (int)data["offset"];
                var limit = (int)data["limit"];
                var id = (string)data["id"];
                GetPage(receivedEvent.ConnectionId, id, offset, limit);
            }
            else
            {
                GetPage(receivedEvent.ConnectionId);
            }
        }

        private void GetPage(string connectionId, string id = "", int offset = 0, int limit = 800)
        {
            var data = _libraryApiAdapter.GetEpisodes(id).ToList();
            var message = PaginatedData.CreateMessage(offset, limit, data, Constants.PodcastEpisodes);
            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}
