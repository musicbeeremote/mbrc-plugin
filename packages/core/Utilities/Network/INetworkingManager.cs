using System;

namespace MusicBeePlugin.Utilities.Network
{
    public interface INetworkingManager : IDisposable
    {
        bool IsRunning { get; }

        void StartListening();
        void StopListening();
        void Restart();
    }
}
