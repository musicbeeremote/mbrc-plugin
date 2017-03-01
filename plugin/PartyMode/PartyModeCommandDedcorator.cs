using MbrcPartyMode;
using MbrcPartyMode.Helper;
using MbrcPartyMode.Model;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.PartyMode
{
    public class PartyModeCommandDedcorator : CommandDecorator
    {
        private readonly ICommand _cmd;
        private readonly PartyModeModel _model;

        public PartyModeCommandDedcorator(ICommand cmd) : base(cmd)
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

                default:
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
            var socketserver = SocketServer.Instance;
            ConnectedClientAddress adr = null;
            if (socketserver != null)
            {
                adr = _model.GetConnectedClientAdresss(eEvent.ClientId, socketserver.GetIpAddress(eEvent.ClientId));
            }

            var isAllowed = PartyModeCommandHandler.Instance.IsCommandAllowed(mc, adr);

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
            PartyModeCommandHandler.Instance.OnServerCommandExecuted(eEvent.ClientId, eEvent.Type, isAllowed);
        }
    }
}