using System;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    public class PingReply : ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            var message = new SocketMessage(Constants.Pong,
                Constants.Reply, DateTime.UtcNow)
                .toJsonString();
            SocketServer.Instance.Send(message);
        }
    }
}