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

        public RequestPluginVersion(ITinyMessengerHub hub)
        {
            _hub = hub;
        }

        public void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PluginVersion, UserSettings.Instance.CurrentVersion);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }
}