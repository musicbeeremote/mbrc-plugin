using System;
using System.Linq;
using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands
{

    public class RequestedCommandNotAllowed : PartyModeBaseCommand
    {
        public override void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send("command not allowed ", eEvent.ClientId);
        }
    }

}