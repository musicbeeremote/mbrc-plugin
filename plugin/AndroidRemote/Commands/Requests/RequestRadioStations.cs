using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestRadioStations:ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestRadioStations(eEvent.ClientId);
        }
    }
}