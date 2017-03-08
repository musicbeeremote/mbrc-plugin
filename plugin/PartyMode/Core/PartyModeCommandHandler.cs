using MusicBeePlugin.AndroidRemote.Commands;
using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.PartyMode.Core.Model;
using TinyMessenger;

namespace MusicBeePlugin.PartyMode.Core
{
    public class PartyModeCommandHandler
    {
        private readonly ITinyMessengerHub _hub;
        private readonly PartyModeModel _partyModeModel;

        #region vars

        private static PartyModeCommandHandler _instance;

        #endregion vars

        public PartyModeCommandHandler(ITinyMessengerHub hub, PartyModeModel partyModeModel)
        {
            _hub = hub;
            _partyModeModel = partyModeModel;
            _hub.Subscribe<ConnectionReadyEvent>(msg => OnClientConnected(msg.Client));
        }

        public bool PartyModeActive => _partyModeModel.Settings.IsActive;

        private void OnClientConnected(SocketConnection client)
        {
            _partyModeModel.AddClientIfNotExists(client);
        }

        public void OnClientDisconnected(RemoteClient client)
        {
            _partyModeModel.RemoveConnection(client);
        }

        public void LogActivity(string client, string command, bool isCmdAllowed)
        {
            _partyModeModel.LogCommand(new ServerCommandEventArgs(client, command, isCmdAllowed));
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
            LogActivity(@event.ClientId, @event.Type, hasPermissions);
            return hasPermissions;
        }
    }
}