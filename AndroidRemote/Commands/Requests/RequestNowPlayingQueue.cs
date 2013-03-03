using System;
using System.Collections.Generic;
using System.Xml.Linq;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestNowPlayingQueue : ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            XElement enqueue = XElement.Parse("<a>" + eEvent.Data + "</a>");
            Dictionary<string, MetaTag> map = new Dictionary<string, MetaTag>
                {
                    {"artist", MetaTag.artist},
                    {"genre", MetaTag.genre},
                    {"album", MetaTag.album},
                    {"title", MetaTag.title},
                    {"none", MetaTag.none}
                };

            Dictionary<string, QueueType> queueMap = new Dictionary<string, QueueType>();
            queueMap.Add("next", QueueType.Next);
            queueMap.Add("last", QueueType.Last);

            MetaTag tag;
            QueueType type;

            string info = (string)enqueue.Element(Constants.Info);

            map.TryGetValue((string) enqueue.Element(Constants.Tag), out tag);
            queueMap.TryGetValue((string) enqueue.Element(Constants.Position), out type);

            Plugin.Instance.RequestQueueFiles(type,tag,info);
        }
    }
}
