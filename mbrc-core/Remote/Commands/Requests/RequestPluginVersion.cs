using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using MusicBeeRemoteCore.Remote.Settings;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
{
    internal class RequestPluginVersion : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly UserSettings _settings;

        public RequestPluginVersion(ITinyMessengerHub hub, UserSettings settings)
        {
            _hub = hub;
            _settings = settings;
        }

        public void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PluginVersion, _settings.CurrentVersion);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }
}