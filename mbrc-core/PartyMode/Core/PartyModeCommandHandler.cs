using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.PartyMode.Core.Model;
using TinyMessenger;

namespace MusicBeeRemote.PartyMode.Core
{
    public class PartyModeCommandHandler
    {
        private readonly ITinyMessengerHub _hub;
        private readonly PartyModeModel _partyModeModel;
          
        public PartyModeCommandHandler(ITinyMessengerHub hub, PartyModeModel partyModeModel)
        {
            _hub = hub;
            _partyModeModel = partyModeModel;
            _hub.Subscribe<ConnectionReadyEvent>(msg => OnClientConnected(msg.Client));
        }

        public bool PartyModeActive => _partyModeModel.Settings.IsActive;

        private void OnClientConnected(SocketConnection connection)
        {
            // A connection where broadcast is disabled is from a secondary connection of an existing client.
            // Each client should only have one active broadcast enabled connection that is the main communication
            // channel.
            if (!connection.BroadcastsEnabled)
            {
                return;
            }

            _partyModeModel.AddClientIfNotExists(connection);
        }

        public void OnClientDisconnected(RemoteClient client)
        {
            _partyModeModel.RemoveConnection(client);
        }

        public bool HasPermissions(ICommand command, IEvent @event)
        {
            var limitedCommand = command as LimitedCommand;
            if (limitedCommand == null)
            {
                return true;
            }

            var client = _partyModeModel.GetClient(@event.ClientId);
            var hasPermissions = client.HasPermission(limitedCommand.GetPermissions());
            _partyModeModel.LogCommand(@event.ClientId, @event.Type, hasPermissions);
           
            return hasPermissions;
        }
    }
}