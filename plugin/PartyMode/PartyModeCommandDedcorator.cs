using mbrcPartyMode;
using mbrcPartyMode.Helper;
using mbrcPartyMode.Model;
using mbrcPartyMode.Tools;
using MusicBeePlugin.AndroidRemote.Commands.Requests;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    public class PartyModeCommandDedcorator : CommandDecorator
    {
        private readonly ICommand cmd;
        private PartyModeModel model;

        public PartyModeCommandDedcorator(ICommand cmd) : base(cmd)
        {
            this.cmd = cmd;
            this.model = PartyModeModel.Instance;
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
            Debug.WriteLine("EventTyp: " + eEvent.Type.ToString());

            MappingCommand mc = PartyModeCommandMapper.MapCommand(cmd);
            var socketserver = SocketServer.Instance;
            ConnectedClientAddress adr = null;
            if (socketserver != null)
            {
                adr = model.GetConnectedClientAdresss(eEvent.ClientId, socketserver.GetIpAddress(eEvent.ClientId));
            }

            bool isAllowed = PartyModeCommandHandler.Instance.IsCommandAllowed(mc, adr) == true;
           
            if (isAllowed)
            {
                BeforeExcute(mc, adr);
                cmd.Execute(eEvent);
            }
            else
            {
                RequestedCommandNotAllowed rmcmd = new RequestedCommandNotAllowed();
                rmcmd.Execute(eEvent);
            }
            PartyModeCommandHandler.Instance.OnServerCommandExecuted(eEvent.ClientId.ToString(), eEvent.Type.ToString(), isAllowed);
        }
    }
}