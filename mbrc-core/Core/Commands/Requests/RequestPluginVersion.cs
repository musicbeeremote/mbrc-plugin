using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Settings;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
{
    internal class RequestPluginVersion : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly PersistenceManager _settings;

        public RequestPluginVersion(ITinyMessengerHub hub, PersistenceManager settings)
        {
            _hub = hub;
            _settings = settings;
        }

        public void Execute(IEvent receivedEvent)
        {
            var message = new SocketMessage(Constants.PluginVersion, _settings.UserSettingsModel.CurrentVersion);
            _hub.Publish(new PluginResponseAvailableEvent(message, receivedEvent.ConnectionId));
        }
    }
}
