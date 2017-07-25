using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
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
            var result = false;
            var token = @event.Data as JToken;

            if (token != null)
            {
                var query = (string) token;
                result = _apiAdapter.PlayMatchingTrack(query);
            }

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
                Context = Constants.NowPlayingQueue,
                NewLineTerminated = true
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

            var token = @event.Data as JToken;
            if (token != null && token.Type == JTokenType.Integer)
            {
                result = _nowPlayingApiAdapter.PlayIndex((int) token);
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
            var token = @event.Data as JToken;
            var index = -1;

            if (token != null && token.Type == JTokenType.Integer)
            {
                index = (int) token;
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
            var from = -1;
            var to = -1;

            var success = false;
            var token = @event.Data as JToken;

            if (token != null && token.Type == JTokenType.Object)
            {
                from = (int) token["from"];
                to = (int) token["to"];
                success = _nowPlayingApiAdapter.MoveTrack(from, to);
            }

            var reply = new
            {
                success,
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