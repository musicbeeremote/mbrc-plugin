using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    class StartServiceBroadcast: ICommand 
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            ServiceDiscovery.Instance.Start();
        }
    }
}
