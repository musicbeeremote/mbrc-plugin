using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestPlayerOutputSwitch: ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.SwitchOutputDevice(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}