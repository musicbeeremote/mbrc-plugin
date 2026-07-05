using MusicBeePlugin.Networking.Server;

namespace MusicBeePlugin.Utilities.Network
{
    public interface IAuthenticator
    {
        bool IsClientAuthenticated(string connectionId);
        bool IsClientBroadcastEnabled(string connectionId);
        void RemoveClientOnDisconnect(string connectionId);
        void AddClientOnConnect(string connectionId);
        SocketClient Client(string connectionId);
    }
}
