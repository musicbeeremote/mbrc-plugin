using System.Collections.Generic;
using MusicBeeRemote.Core.Events;
using Newtonsoft.Json.Linq;
using static MusicBeeRemote.Core.Commands.Requests.PaginatedData;

namespace MusicBeeRemote.Core.Commands.Requests
{
    public abstract class PaginatedDataCommand<T> : ICommand
    {
        public void Execute(IEvent @event)
        {
            var data = @event.Data as JObject;
            if (data != null)
            {
                var offset = (int) data["offset"];
                var limit = (int) data["limit"];
                GetPage(@event.ConnectionId, offset, limit);
            }
            else
            {
                GetPage(@event.ConnectionId);
            }
        }

        protected abstract List<T> GetData();

        protected abstract string Context();

        internal abstract void Publish(PluginResponseAvailableEvent @event);

        private void GetPage(string connectionId, int offset = 0, int limit = 800)
        {
            Publish(new PluginResponseAvailableEvent(CreateMessage(offset, limit, GetData(), Context()), connectionId));
        }
    }
}