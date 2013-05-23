using System;
using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestLibQueueGenre:ICommand
    {
        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void Execute(IEvent eEvent)
        {
            string type, query;

            ((Dictionary<string, string>)eEvent.Data).TryGetValue("type", out type);
            ((Dictionary<string, string>)eEvent.Data).TryGetValue("query", out query);
            QueueType qType = type == "next" ? QueueType.Next : QueueType.Last;
            Plugin.Instance.RequestQueueFiles(qType, MetaTag.genre, query);
        }
    }
}
