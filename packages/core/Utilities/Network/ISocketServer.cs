using System;
using MusicBeePlugin.Protocol.Messages;

namespace MusicBeePlugin.Utilities.Network
{
    public interface ISocketServer : IDisposable
    {
        bool IsRunning { get; }

        void StartListening();
        void StopListening();
        void RestartSocket();
        void KickClient(string connectionId);
        void Send(string message, string connectionId);
        void Send(string message);
        void Broadcast(BroadcastEvent broadcastEvent);
    }
}
