using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using MusicBeePlugin.AndroidRemote.Interfaces;
using NLog;
using mbrcPartyMode;
using mbrcPartyMode.Helper;

namespace MusicBeePlugin.AndroidRemote.Commands
{

    public abstract class CommandDecorator : ICommand
    {
        private ICommand command;


        protected CommandDecorator(ICommand cmd)
        {
            this.command = cmd;
        }

      

        public virtual void Execute(IEvent eEvent)
        {
           
            command.Execute(eEvent);
        }
    }


}