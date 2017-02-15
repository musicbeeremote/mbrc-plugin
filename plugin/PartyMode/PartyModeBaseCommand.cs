using System;
using System.Linq;
using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands
{

    public abstract class PartyModeBaseCommand : ICommand
    {
        public abstract void Execute(IEvent eEvent);
    }

}