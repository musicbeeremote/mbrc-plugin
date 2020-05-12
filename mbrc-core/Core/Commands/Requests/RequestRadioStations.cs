using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
{
    internal class RequestRadioStations : ICommand
    {
        private readonly ILibraryApiAdapter _libraryApiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestRadioStations(ILibraryApiAdapter libraryApiAdapter, ITinyMessengerHub hub)
        {
            _libraryApiAdapter = libraryApiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent.Data is JObject data)
            {
                var offset = (int)data["offset"];
                var limit = (int)data["limit"];
                GetPage(receivedEvent.ConnectionId, offset, limit);
            }
            else
            {
                GetPage(receivedEvent.ConnectionId);
            }
        }

        private void GetPage(string connectionId, int offset = 0, int limit = 800)
        {
            var stations = _libraryApiAdapter.GetRadioStations().ToList();
            var total = stations.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = Constants.RadioStations,
                Data = new Page<RadioStation>
                {
                    Data = offset > total ? new List<RadioStation>() : stations.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total,
                },
                NewLineTerminated = true,
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}
