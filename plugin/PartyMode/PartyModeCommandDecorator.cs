using System.Net;
using MusicBeePlugin.AndroidRemote.Commands;
using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Commands.Requests;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.PartyMode.Core;
using MusicBeePlugin.PartyMode.Core.Model;

namespace MusicBeePlugin.PartyMode
{
    public class PartyModeCommandDecorator : CommandDecorator
    {
        private readonly ICommand _cmd;
        private readonly PartyModeModel _model;

        public PartyModeCommandDecorator(ICommand cmd) : base(cmd)
        {
            _cmd = cmd;
            _model = PartyModeModel.Instance;
        }

        public override void Execute(IEvent eEvent)
        {
            if (!_model.Settings.IsActive)
            {
                _cmd.Execute(eEvent);
                return;
            }

            var partyModeCommandHandler = PartyModeCommandHandler.Instance;

            if (_cmd is ClientConnected)
            {
                _cmd.Execute(eEvent);

                var clientAddress = eEvent.Data as IPAddress;
                if (clientAddress != null && IPAddress.IsLoopback(clientAddress))
                {
                    return;
                }
                partyModeCommandHandler.OnClientConnected(GetRemoteClient(eEvent));
            }
            else if (_cmd is ClientDisconnected)
            {
                partyModeCommandHandler.OnClientDisconnected(GetRemoteClient(eEvent));
            }

            var client = GetRemoteClient(eEvent);
            if (_cmd is RequestNowplayingQueue && partyModeCommandHandler.ClientCanOnlyAdd(client))
            {
                var cmd = new RequestNowplayingPartyQueue();
                cmd.Execute(eEvent);
                partyModeCommandHandler.OnServerCommandExecuted(client.ClientId, "add", true);
                return;
            }

            var canExecute = true;
            var limitedCommand = _cmd as LimitedCommand;
            if (limitedCommand != null)
            {
                var command = limitedCommand;
                canExecute = client.HasPermission(command.GetPermissions());
                if (canExecute)
                {
                    _cmd.Execute(eEvent);
                }
                else
                {
                    var rmcmd = new RequestedCommandNotAllowed();
                    rmcmd.Execute(eEvent);
                }
            }
            else
            {
                _cmd.Execute(eEvent);
            }

            partyModeCommandHandler.OnServerCommandExecuted(client.ClientId, eEvent.Type, canExecute);
        }

        private RemoteClient GetRemoteClient(IEvent eEvent)
        {
            var ipAddress = Authenticator.GetIpAddress(eEvent.ConnectionId);
            var clientId = Authenticator.ClientId(eEvent.ConnectionId);
            var client = _model.GetClientAddress(clientId, ipAddress);
            return client;
        }
    }
}