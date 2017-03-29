using System.Collections.Generic;
using System.Linq;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Enumerations;
using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using MusicBeeRemoteCore.Remote.Utilities;
using Newtonsoft.Json.Linq;
using NLog;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
{
    internal class RequestNowPlayingSearch : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _apiAdapter;

        public RequestNowPlayingSearch(INowPlayingApiAdapter apiAdapter, ITinyMessengerHub hub)
        {
            _apiAdapter = apiAdapter;
            _hub = hub;
        }

        public override void Execute(IEvent @event)
        {
            var result = _apiAdapter.PlayMatchingTrack(@event.DataToString());
            var message = new SocketMessage(Constants.NowPlayingListSearch, result);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback;
    }

    public class RequestNowplayingQueue : LimitedCommand
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ITinyMessengerHub _hub;
        private readonly IQueueAdapter _queueAdapter;

        public RequestNowplayingQueue(ITinyMessengerHub hub, IQueueAdapter queueAdapter)
        {
            _hub = hub;
            _queueAdapter = queueAdapter;
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback |
                                                               CommandPermissions.AddTrack;

        public override void Execute(IEvent @event)
        {
            var payload = @event.Data as JObject;
            if (payload == null)
            {
                Logger.Debug("there was no payload in the request");
                return;
            }
            var queueType = (string) payload["queue"];
            var data = (JArray) payload["data"];
            var play = (string) payload["play"];

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

            var success = _queueAdapter.QueueFiles(queue, data.Select(c => (string) c).ToArray(), play);

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
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _nowPlayingApiAdapter;

        public RequestNowPlayingPlay(ITinyMessengerHub hub, INowPlayingApiAdapter nowPlayingApiAdapter)
        {
            _hub = hub;
            _nowPlayingApiAdapter = nowPlayingApiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var result = false;

            int trackIndex;
            if (int.TryParse(@event.DataToString(), out trackIndex))
            {
                result = _nowPlayingApiAdapter.PlayIndex(trackIndex);
            }

            var message = new SocketMessage(Constants.NowPlayingListPlay, result);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback;
    }

    internal class RequestNowPlayingTrackRemoval : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _nowPlayingApiAdapter;

        public RequestNowPlayingTrackRemoval(ITinyMessengerHub hub, INowPlayingApiAdapter nowPlayingApiAdapter)
        {
            _hub = hub;
            _nowPlayingApiAdapter = nowPlayingApiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var success = false;
            int index;
            if (int.TryParse(@event.DataToString(), out index))
            {
                success = _nowPlayingApiAdapter.RemoveIndex(index);
            }

            var reply = new
            {
                success,
                index
            };

            var message = new SocketMessage(Constants.NowPlayingListRemove, reply);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.RemoveTrack;
    }

    public class RequestNowplayingPartyQueue : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IQueueAdapter _queueAdapter;

        public RequestNowplayingPartyQueue(ITinyMessengerHub hub, IQueueAdapter queueAdapter)
        {
            _hub = hub;
            _queueAdapter = queueAdapter;
        }

        public void Execute(IEvent @event)
        {
            var payload = @event.Data as JObject;
            if (payload == null)
            {
                const int code = 400;
                SendResponse(@event.ConnectionId, code);
                return;
            }

            var data = (JArray) payload["data"];

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
                var success = _queueAdapter.QueueFiles(QueueType.Last, data.Select(c => (string) c).ToArray());
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
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _nowPlayingApiAdapter;

        public RequestNowPlayingMoveTrack(ITinyMessengerHub hub, INowPlayingApiAdapter nowPlayingApiAdapter)
        {
            _hub = hub;
            _nowPlayingApiAdapter = nowPlayingApiAdapter;
        }

        public void Execute(IEvent @event)
        {
            int from, to;
            string sFrom, sTo;

            ((Dictionary<string, string>) @event.Data).TryGetValue("from", out sFrom);
            ((Dictionary<string, string>) @event.Data).TryGetValue("to", out sTo);
            int.TryParse(sFrom, out from);
            int.TryParse(sTo, out to);

            var reply = new
            {
                success = _nowPlayingApiAdapter.MoveTrack(from, to),
                from,
                to
            };

            var message = new SocketMessage(Constants.NowPlayingListMove, reply);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestNowPlayingList : ICommand
    {
        private readonly Authenticator _auth;
        private readonly ITinyMessengerHub _hub;
        private readonly INowPlayingApiAdapter _nowPlayingApiAdapter;

        public RequestNowPlayingList(Authenticator auth,
            ITinyMessengerHub hub,
            INowPlayingApiAdapter nowPlayingApiAdapter)
        {
            _auth = auth;
            _hub = hub;
            _nowPlayingApiAdapter = nowPlayingApiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var socketClient = _auth.GetConnection(@event.ConnectionId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            var data = @event.Data as JObject;
            if (clientProtocol < 2.2 || data == null)
            {
                var tracks = _nowPlayingApiAdapter.GetTracksLegacy().ToList();
                var message = new SocketMessage(Constants.NowPlayingList, tracks);
                _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
            }
            else
            {
                var offset = (int) data["offset"];
                var limit = (int) data["limit"];
                var tracks = _nowPlayingApiAdapter.GetTracks(offset, limit).ToList();
                var total = tracks.Count;
                var realLimit = offset + limit > total ? total - offset : limit;
                var message = new SocketMessage
                {
                    Context = Constants.NowPlayingList,
                    Data = new Page<NowPlaying>
                    {
                        Data = offset > total ? new List<NowPlaying>() : tracks.GetRange(offset, realLimit),
                        Offset = offset,
                        Limit = limit,
                        Total = total
                    },
                    NewLineTerminated = true
                };

                _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
            }
        }
    }
}