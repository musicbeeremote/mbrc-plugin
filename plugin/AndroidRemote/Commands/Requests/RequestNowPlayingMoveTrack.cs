using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingMoveTrack : ICommand
    {
        public void Dispose()
        {  
        }

        public void Execute(IEvent eEvent)
        {
            int from, to;
            string sFrom, sTo;

            ((Dictionary<string, string>)eEvent.Data).TryGetValue("from",out sFrom);
            ((Dictionary<string, string>)eEvent.Data).TryGetValue("to", out sTo);
            int.TryParse(sFrom, out from);
            int.TryParse(sTo, out to);
            Plugin.Instance.RequestNowPlayingMove(eEvent.ClientId, from, to);
        }
    }
}
