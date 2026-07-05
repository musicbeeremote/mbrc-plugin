using System;

namespace MusicBeePlugin.Utilities.Network
{
    public interface IProtocolHandler
    {
        event Action<string> ForceClientDisconnect;
        void ProcessIncomingMessage(string incomingMessage, string connectionId);
    }
}
