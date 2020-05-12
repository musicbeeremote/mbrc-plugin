using System;
using System.Collections.Generic;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using Newtonsoft.Json.Linq;
using static MusicBeeRemote.Core.Commands.Requests.PaginatedData;

namespace MusicBeeRemote.Core.Commands.Requests
{
    public abstract class PaginatedDataCommand<T> : ICommand
    {
        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent == null)
            {
                throw new ArgumentNullException(nameof(receivedEvent));
            }

            if (receivedEvent.Data is JObject data)
            {
                var offset = (int)data["offset"];
                var limit = (int)data["limit"];
                GetPage(receivedEvent.ConnectionId, offset, limit);
            }
            else
            {
                GetPage(receivedEvent.ConnectionId);
            }
        }

        internal abstract void Publish(PluginResponseAvailableEvent @event);

        protected abstract List<T> GetData();

        protected abstract string Context();

        private void GetPage(string connectionId, int offset = 0, int limit = 800)
        {
            Publish(new PluginResponseAvailableEvent(CreateMessage(offset, limit, GetData(), Context()), connectionId));
        }
    }
}
