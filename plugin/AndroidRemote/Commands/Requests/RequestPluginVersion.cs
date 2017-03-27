using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using TinyMessenger;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
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