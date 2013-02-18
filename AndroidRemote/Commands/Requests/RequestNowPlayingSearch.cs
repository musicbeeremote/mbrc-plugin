using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestNowPlayingSearch :ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.NowPlayingSearch(eEvent.Data);
        }
    }
}
