using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class BroadcastEventAvailable : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            if (eEvent.Data is BroadcastEvent broadcastEvent) SocketServer.Instance.Broadcast(broadcastEvent);
        }
    }
}