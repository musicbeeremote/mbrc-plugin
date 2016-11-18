using System;
using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestNowplayingQueue : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var payload = eEvent.Data as JsonObject;
            var queueType = payload.Get<string>("queue");
            var data = payload.Get<List<string>>("data");

            if (data == null)
            {
                const int code = 400;
                SendResponse(eEvent.ClientId, code);
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

            var success = Plugin.Instance.QueueFiles(queue, data.ToArray());

            SendResponse(eEvent.ClientId, success ? 200 : 500);
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
}