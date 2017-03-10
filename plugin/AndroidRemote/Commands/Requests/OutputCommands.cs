using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestOutputDeviceList : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestOutputDevice(@event.ConnectionId);
        }
    }

    internal class RequestPlayerOutputSwitch : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.SwitchOutputDevice(@event.DataToString(), @event.ConnectionId);
        }
    }
}