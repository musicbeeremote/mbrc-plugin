using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestRadioStations : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            if (eEvent.Data is JsonObject data)
            {
                var offset = data.Get<int>("offset");
                var limit = data.Get<int>("limit");
                Plugin.Instance.RequestRadioStations(eEvent.ClientId, offset, limit);
            }
            else
            {
                Plugin.Instance.RequestRadioStations(eEvent.ClientId);
            }
        }
    }
}