using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestNowPlayingMoveTrack : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            ((Dictionary<string, string>)eEvent.Data).TryGetValue("from", out var sFrom);
            ((Dictionary<string, string>)eEvent.Data).TryGetValue("to", out var sTo);
            int.TryParse(sFrom, out var from);
            int.TryParse(sTo, out var to);
            Plugin.Instance.RequestNowPlayingMove(eEvent.ClientId, from, to);
        }
    }
}