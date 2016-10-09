using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote
{
    internal class BroadcastEventAvailable : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var broadcastEvent = eEvent.Data as BroadcastEvent;
            if (broadcastEvent != null)
            {
                SocketServer.Instance.Broadcast(broadcastEvent);
            }
        }
    }
}