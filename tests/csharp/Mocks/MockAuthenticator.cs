using System.Collections.Generic;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Utilities.Network;

namespace MusicBeeRemote.Core.Tests.Mocks
{
    /// <summary>
    /// Mock authenticator for testing protocol handshake and client management.
    /// </summary>
    public class MockAuthenticator : IAuthenticator
    {
        private readonly Dictionary<string, SocketClient> _clients = new Dictionary<string, SocketClient>();

        public void AddClient(SocketClient client)
        {
            _clients[client.ConnectionId] = client;
        }

        public void AddClientOnConnect(string connectionId)
        {
            _clients[connectionId] = new SocketClient(connectionId);
        }

        public SocketClient Client(string connectionId)
        {
            return _clients.TryGetValue(connectionId, out var client) ? client : null;
        }

        public bool IsClientAuthenticated(string connectionId)
        {
            return _clients.TryGetValue(connectionId, out var client) && client.Authenticated;
        }

        public bool IsClientBroadcastEnabled(string connectionId)
        {
            return _clients.TryGetValue(connectionId, out var client) && client.BroadcastsEnabled;
        }

        public void RemoveClientOnDisconnect(string connectionId)
        {
            _clients.Remove(connectionId);
        }
    }
}
