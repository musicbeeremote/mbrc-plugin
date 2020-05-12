using System;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.NowPlayingCommands
{
    public class RequestNowPlayingQueue : LimitedCommand
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly ITinyMessengerHub _hub;
        private readonly IQueueAdapter _queueAdapter;

        public RequestNowPlayingQueue(ITinyMessengerHub hub, IQueueAdapter queueAdapter)
        {
            _hub = hub;
            _queueAdapter = queueAdapter;
        }

        /// <inheritdoc />
        public override string Name() => "Now Playing: Queue";

        public override void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            if (!(receivedEvent.Data is JObject payload))
            {
                _logger.Debug("there was no payload in the request");
                return;
            }

            var queueType = (string)payload["queue"];
            var data = (JArray)payload["data"];
            var play = (string)payload["play"];

            if (data == null)
            {
                const int code = 400;
                SendResponse(receivedEvent.ConnectionId, code);
                return;
            }

            var queue = QueueType.PlayNow;
            if (queueType.Equals("next", StringComparison.InvariantCultureIgnoreCase))
            {
                queue = QueueType.Next;
            }
            else if (queueType.Equals("last", StringComparison.InvariantCultureIgnoreCase))
            {
                queue = QueueType.Last;
            }
            else if (queueType.Equals("add-all", StringComparison.InvariantCultureIgnoreCase))
            {
                queue = QueueType.AddAndPlay;
            }

            var success = _queueAdapter.QueueFiles(queue, data.Select(c => (string)c).ToArray(), play);

            SendResponse(receivedEvent.ConnectionId, success ? 200 : 500);
        }

        protected override CommandPermissions GetPermissions()
        {
            return CommandPermissions.StartPlayback |
                   CommandPermissions.AddTrack;
        }

        private void SendResponse(string connectionId, int code)
        {
            var queueResponse = new QueueResponse { Code = code };
            var message = new SocketMessage
            {
                Data = queueResponse,
                Context = Constants.NowPlayingQueue,
                NewLineTerminated = true,
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }
}
