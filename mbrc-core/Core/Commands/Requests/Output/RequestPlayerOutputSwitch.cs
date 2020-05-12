using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests.Output
{
    internal class RequestPlayerOutputSwitch : ICommand
    {
        private readonly IOutputApiAdapter _apiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestPlayerOutputSwitch(IOutputApiAdapter apiAdapter, ITinyMessengerHub hub)
        {
            _apiAdapter = apiAdapter;
            _hub = hub;
        }

        public void Execute(IEvent receivedEvent)
        {
            if (receivedEvent.Data is JToken token && token.Type == JTokenType.String)
            {
                var device = (string)token;
                _apiAdapter.SetOutputDevice(device);
            }

            var message = new SocketMessage(Constants.PlayerOutput, _apiAdapter.GetOutputDevices())
            {
                NewLineTerminated = true,
            };
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }
    }
}
