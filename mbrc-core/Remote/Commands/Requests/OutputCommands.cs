using MusicBeeRemoteCore.ApiAdapters;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
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