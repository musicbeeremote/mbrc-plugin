using System;

namespace MusicBeePlugin.Utilities.Network
{
    public interface IServiceDiscovery : IDisposable
    {
        void StartListening();
        void StopListening();
    }
}
