using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
{
    internal class RequestOutputDeviceList : ICommand
    {
        private readonly IOutputApiAdapter _apiAdapter;
        private readonly ITinyMessengerHub _hub;


        public RequestOutputDeviceList(IOutputApiAdapter apiAdapter, ITinyMessengerHub hub)
        {
            _apiAdapter = apiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerOutput, _apiAdapter.GetOutputDevices());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestPlayerOutputSwitch : ICommand
    {
        private readonly IOutputApiAdapter _apiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPlayerOutputSwitch(IOutputApiAdapter apiAdapter, ITinyMessengerHub hub)
        {
            _apiAdapter = apiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            _apiAdapter.SetOutputDevice(@event.DataToString());
            var message = new SocketMessage(Constants.PlayerOutput, _apiAdapter.GetOutputDevices());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }
}