using MbrcPartyMode;
using MbrcPartyMode.Helper;
using MbrcPartyMode.Model;
using MusicBeePlugin.AndroidRemote.Commands.Requests;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;

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

        public void BeforeExcute(MappingCommand mc, ConnectedClientAddress adr)
        {
            switch (mc)
            {
                case MappingCommand.ClientConnected:
                    PartyModeCommandHandler.Instance.OnClientConnected(adr);
                    break;

                case MappingCommand.ClientDisconnected:
                    PartyModeCommandHandler.Instance.OnClientDisconnected(adr);
                    break;

                case MappingCommand.StopServer:
                    PartyModeModel.Instance.SaveSettings();
                    break;
            }
        }

        public override void Execute(IEvent eEvent)
        {
            if (!_model.Settings.IsActive)
            {
                _cmd.Execute(eEvent);
                return;
            }

            var mc = PartyModeCommandMapper.MapCommand(_cmd);

            var ipAddress = Authenticator.GetIpAddress(eEvent.ConnectionId);
            var clientId = Authenticator.ClientId(eEvent.ConnectionId);
            var adr = _model.GetConnectedClientAdresss(clientId, ipAddress);


            var partyModeCommandHandler = PartyModeCommandHandler.Instance;
            var isAllowed = partyModeCommandHandler.IsCommandAllowed(mc, adr);

            if (_cmd is RequestNowplayingQueue && partyModeCommandHandler.ClientCanOnlyAdd(adr))
            {
                var cmd = new RequestNowplayingPartyQueue();
                cmd.Execute(eEvent);
                partyModeCommandHandler.OnServerCommandExecuted(adr.ClientId, "add", true);
                return;
            }

            if (isAllowed)
            {
                BeforeExcute(mc, adr);
                _cmd.Execute(eEvent);
            }
            else
            {
                var rmcmd = new RequestedCommandNotAllowed();
                rmcmd.Execute(eEvent);
            }

            partyModeCommandHandler.OnServerCommandExecuted(adr.ClientId, eEvent.Type, isAllowed);
        }
    }
}