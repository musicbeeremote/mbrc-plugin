using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestOutputDeviceList : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestOutputDevice(eEvent.ConnectionId);
        }
    }

    internal class RequestPlayerOutputSwitch : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.SwitchOutputDevice(eEvent.DataToString(), eEvent.ConnectionId);
        }
    }
}