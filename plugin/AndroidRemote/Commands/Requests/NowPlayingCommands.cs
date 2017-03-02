using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using ServiceStack.Text;
using static MusicBeePlugin.AndroidRemote.Commands.CommandPermissions;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingSearch : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.NowPlayingSearch(eEvent.DataToString(), eEvent.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => StartPlayback;
    }

    public class RequestNowplayingQueue : LimitedCommand
    {
        public override CommandPermissions GetPermissions() => StartPlayback | AddTrack;

        public override void Execute(IEvent eEvent)
        {
            var payload = eEvent.Data as JsonObject;
            var queueType = payload.Get<string>("queue");
            var data = payload.Get<List<string>>("data");
            var play = payload.Get<string>("play");

            if (data == null)
            {
                const int code = 400;
                SendResponse(eEvent.ConnectionId, code);
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

            var success = Plugin.Instance.QueueFiles(queue, data.ToArray(), play);

            SendResponse(eEvent.ConnectionId, success ? 200 : 500);
        }

        private static void SendResponse(string clientId, int code)
        {
            var queueResponse = new QueueResponse
            {
                Code = code
            };
            var socketMessage = new SocketMessage
            {
                Data = queueResponse,
                Context = Constants.NowPlayingQueue
            };
            SocketServer.Instance.Send(socketMessage.SerializeToString(), clientId);
        }
    }

    internal class RequestNowPlayingPlay : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.NowPlayingPlay(eEvent.DataToString());
        }
    }

    internal class RequestNowPlayingTrackRemoval : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            int index;
            if (int.TryParse(eEvent.DataToString(), out index))
            {
                Plugin.Instance.NowPlayingListRemoveTrack(index, eEvent.ConnectionId);
            }
        }

        public override CommandPermissions GetPermissions() => RemoveTrack;
    }

    public class RequestNowplayingPartyQueue : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var payload = eEvent.Data as JsonObject;
            var data = payload.Get<List<string>>("data");

            if (data == null)
            {
                const int code = 400;
                SendResponse(eEvent.ConnectionId, code);
                return;
            }

            if (data.Count > 1)
            {
                SendResponse(eEvent.ConnectionId, 403);
            }
            else
            {
                var success = Plugin.Instance.QueueFiles(QueueType.Last, data.ToArray());
                SendResponse(eEvent.ConnectionId, success ? 200 : 500);
            }
        }

        private static void SendResponse(string clientId, int code)
        {
            var queueResponse = new QueueResponse
            {
                Code = code
            };
            var socketMessage = new SocketMessage
            {
                Data = queueResponse,
                Context = Constants.NowPlayingQueue
            };
            SocketServer.Instance.Send(socketMessage.SerializeToString(), clientId);
        }
    }

    internal class RequestNowPlayingMoveTrack : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            int from, to;
            string sFrom, sTo;

            ((Dictionary<string, string>) eEvent.Data).TryGetValue("from", out sFrom);
            ((Dictionary<string, string>) eEvent.Data).TryGetValue("to", out sTo);
            int.TryParse(sFrom, out from);
            int.TryParse(sTo, out to);
            Plugin.Instance.RequestNowPlayingMove(eEvent.ConnectionId, from, to);
        }
    }

    internal class RequestNowPlayingList : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var socketClient = Authenticator.GetConnection(eEvent.ConnectionId);
            var clientProtocol = socketClient?.ClientProtocolVersion ?? 2.1;

            var data = eEvent.Data as JsonObject;
            if (clientProtocol < 2.2 || data == null)
            {
                Plugin.Instance.RequestNowPlayingList(eEvent.ConnectionId);
            }
            else
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.RequestNowPlayingListPage(eEvent.ConnectionId, offset, limit);
            }
        }
    }
}