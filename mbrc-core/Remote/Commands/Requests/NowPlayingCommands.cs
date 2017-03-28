using System.Collections.Generic;
using MusicBeeRemoteCore.ApiAdapters;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Enumerations;
using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using MusicBeeRemoteCore.Remote.Utilities;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
{
    internal class RequestNowPlayingSearch : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.NowPlayingSearch(@event.DataToString(), @event.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback;
    }

    public class RequestNowplayingQueue : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IQueueAdapter _queueAdapter;

        public RequestNowplayingQueue(ITinyMessengerHub hub, IQueueAdapter queueAdapter)
        {
            _hub = hub;
            _queueAdapter = queueAdapter;
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback | CommandPermissions.AddTrack;

        public override void Execute(IEvent @event)
        {
            var payload = @event.Data as JsonObject;
            var queueType = payload.Get<string>("queue");
            var data = payload.Get<List<string>>("data");
            var play = payload.Get<string>("play");

            if (data == null)
            {
                const int code = 400;
                SendResponse(@event.ConnectionId, code);
                return;
            }
            var queue = QueueType.PlayNow;
            if (queueType.Equals("next"))
            {
                queue = QueueType.Next;
            }
            else if (queueType.Equals("last"))
            {
                queue = QueueType.Last;
            }
            else if (queueType.Equals("add-all"))
            {
                queue = QueueType.AddAndPlay;
            }

            var success = _queueAdapter.QueueFiles(queue, data.ToArray(), play);

            SendResponse(@event.ConnectionId, success ? 200 : 500);
        }

        private void SendResponse(string connectionId, int code)
        {
            var queueResponse = new QueueResponse
            {
                Code = code
            };
            var message = new SocketMessage
            {
                Data = queueResponse,
                Context = Constants.NowPlayingQueue
            };
            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }

    internal class RequestNowPlayingPlay : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.NowPlayingPlay(@event.DataToString());
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback;
    }

    internal class RequestNowPlayingTrackRemoval : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            int index;
            if (int.TryParse(@event.DataToString(), out index))
            {
                Plugin.Instance.NowPlayingListRemoveTrack(index, @event.ConnectionId);
            }
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.RemoveTrack;
    }

    public class RequestNowplayingPartyQueue : ICommand
    {
        private readonly ITinyMessengerHub _hub;

        public RequestNowplayingPartyQueue(ITinyMessengerHub hub)
        {
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            var payload = @event.Data as JsonObject;
            var data = payload.Get<List<string>>("data");

            if (data == null)
            {
                const int code = 400;
                SendResponse(@event.ConnectionId, code);
                return;
            }

            if (data.Count > 1)
            {
                SendResponse(@event.ConnectionId, 403);
            }
            else
            {
                var success = Plugin.Instance.QueueFiles(QueueType.Last, data.ToArray());
                SendResponse(@event.ConnectionId, success ? 200 : 500);
            }
        }

        private void SendResponse(string connectionId, int code)
        {
            var queueResponse = new QueueResponse
            {
                Code = code
            };
            var message = new SocketMessage
            {
                Data = queueResponse,
                Context = Constants.NowPlayingQueue
            };

            _hub.Publish(new PluginResponseAvailableEvent(message, connectionId));
        }
    }

    internal class RequestNowPlayingMoveTrack : ICommand
    {
        public void Execute(IEvent @event)
        {
            int from, to;
            string sFrom, sTo;

            ((Dictionary<string, string>) @event.Data).TryGetValue("from", out sFrom);
            ((Dictionary<string, string>) @event.Data).TryGetValue("to", out sTo);
            int.TryParse(sFrom, out from);
            int.TryParse(sTo, out to);
            Plugin.Instance.RequestNowPlayingMove(@event.ConnectionId, from, to);
        }
    }

    internal class RequestNowPlayingList : ICommand
    {
        private readonly Authenticator _auth;

        public RequestNowPlayingList(Authenticator auth)
        {
            _auth = auth;
        }

        public void Execute(IEvent @event)
        {
            var socketClient = _auth.GetConnection(@event.ConnectionId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            var data = @event.Data as JsonObject;
            if (clientProtocol < 2.2 || data == null)
            {
                Plugin.Instance.RequestNowPlayingList(@event.ConnectionId);
            }
            else
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.RequestNowPlayingListPage(@event.ConnectionId, offset, limit);
            }
        }
    }
}