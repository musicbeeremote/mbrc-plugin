using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.PartyMode
{
    public class RequestedCommandNotAllowed : PartyModeBaseCommand
    {
        public override void Execute(IEvent eEvent)
        {
            var message = new SocketMessage(Constants.CommandUnavailable, string.Empty)
                .ToJsonString();
            SocketServer.Instance.Send(message, eEvent.ConnectionId);
        }
    }
}