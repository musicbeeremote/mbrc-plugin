using System;
using System.Linq;
using MusicBeeRemote.Core.Caching;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Library
{
    public class RequestBrowseTracks : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackRepository _trackRepository;

        public RequestBrowseTracks(ITinyMessengerHub hub, ITrackRepository trackRepository)
        {
            _hub = hub;
            _trackRepository = trackRepository;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            if (receivedEvent.Data is JObject data)
            {
                var offset = (int)data["offset"];
                var limit = (int)data["limit"];
                SendPage(receivedEvent.ConnectionId, offset, limit);
            }
            else
            {
                SendPage(receivedEvent.ConnectionId);
            }
        }

        private void SendPage(string connectionId, int offset = 0, int limit = 4000)
        {
            var total = _trackRepository.Count();
            var realLimit = offset + limit > total ? total - offset : limit;
            var tracks = _trackRepository.GetRange(offset, realLimit);
            var message = new SocketMessage
            {
                Context = Constants.LibraryBrowseTracks,
                Data = new Page<Track> { Data = tracks.ToList(), Offset = offset, Limit = limit, Total = total },
                NewLineTerminated = true,
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}
