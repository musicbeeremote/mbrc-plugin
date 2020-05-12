using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Output
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

        public void Execute(IEvent receivedEvent)
        {
            var message = new SocketMessage(Constants.PlayerOutput, _apiAdapter.GetOutputDevices())
            {
                NewLineTerminated = true,
            };
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }
    }
}
