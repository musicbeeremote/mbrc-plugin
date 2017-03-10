using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestRadioStations : ICommand
    {
        public void Execute(IEvent @event)
        {
            var data = @event.Data as JsonObject;
            if (data != null)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.RequestRadioStations(@event.ConnectionId, offset, limit);
            }
            else
            {
                Plugin.Instance.RequestRadioStations(@event.ConnectionId);
            }
        }
    }
}