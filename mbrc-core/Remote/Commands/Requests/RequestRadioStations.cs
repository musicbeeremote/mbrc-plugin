using System.Collections.Generic;
using System.Linq;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Entities;
using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
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

        public void Execute(IEvent @event)
        {
            var data = @event.Data as JObject;
            if (data != null)
            {
                var offset = (int) data["offset"];
                var limit = (int) data["limit"];
                GetPage(@event.ConnectionId, offset, limit);
            }
            else
            {
                GetPage(@event.ConnectionId);
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
                    Total = total
                },
                NewLineTerminated = true
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}