using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.Services.Interfaces;
using ServiceStack;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestNowPlayingQueue : ICommand
    {
        private readonly INowPlayingService _nowPlayingService;

        public RequestNowPlayingQueue(INowPlayingService nowPlayingService)
        {
            _nowPlayingService = nowPlayingService;
        }

        public void Execute(IEvent eEvent)
        {
            var payload = eEvent.Data as JsonObject;
            var queueType = payload.Get<string>("queue");
            var data = payload.Get<List<string>>("data");
            var play = payload.Get<string>("play");

            if (data == null)
            {
                const int code = 400;
                SendResponse(eEvent.ClientId, code);
                return;
            }

            var queue = QueueType.PlayNow;
            if (queueType.Equals("next"))
                queue = QueueType.Next;
            else if (queueType.Equals("last"))
                queue = QueueType.Last;
            else if (queueType.Equals("add-all")) queue = QueueType.AddAndPlay;

            var success = _nowPlayingService.QueueFiles(queue, data.ToArray(), play);

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