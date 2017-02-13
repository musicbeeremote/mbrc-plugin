using System;
using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;
using NLog;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestLibQueueArtist : ICommand
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            try
            {
                string type, query;

                ((Dictionary<string, string>) eEvent.Data).TryGetValue("type", out type);
                ((Dictionary<string, string>) eEvent.Data).TryGetValue("query", out query);

                QueueType qType;
                switch (type)
                {
                    case "next":
                        qType = QueueType.Next;
                        break;
                    case "last":
                        qType = QueueType.Last;
                        break;
                    case "now":
                        qType = QueueType.PlayNow;
                        break;
                    default:
                        qType = QueueType.Next;
                        break;
                }

                Plugin.Instance.RequestQueueFiles(qType, MetaTag.artist, query);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to queue artist");
            }
        }
    }
}