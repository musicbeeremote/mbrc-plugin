using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestPause : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPausePlayback(eEvent.ClientId);
        }
    }
}
