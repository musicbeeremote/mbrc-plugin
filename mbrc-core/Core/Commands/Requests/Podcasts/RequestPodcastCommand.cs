using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Podcasts;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Podcasts
{
    public class RequestPodcastCommand : PaginatedDataCommand<PodcastSubscription>
    {
        private readonly ILibraryApiAdapter _libraryApiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPodcastCommand(ILibraryApiAdapter libraryApiAdapter, ITinyMessengerHub hub)
        {
            _libraryApiAdapter = libraryApiAdapter;
            _hub = hub;
        }

        internal override void Publish(PluginResponseAvailableEvent @event)
        {
            _hub.Publish(@event);
        }

        protected override List<PodcastSubscription> GetData()
        {
            return _libraryApiAdapter.GetPodcastSubscriptions().ToList();
        }

        protected override string Context()
        {
            return Constants.PodcastSubscriptions;
        }
    }
}
