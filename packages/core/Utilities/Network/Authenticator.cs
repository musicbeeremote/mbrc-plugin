using System.Collections.Concurrent;
using MusicBeePlugin.Networking.Server;

namespace MusicBeePlugin.Utilities.Network
{
    /// <summary>
    ///     Responsible for the client authentication. Keeps a list of the connected clients.
    /// </summary>
    public class Authenticator : IAuthenticator
    {
        private readonly ConcurrentDictionary<string, SocketClient> _connectedClients =
            new ConcurrentDictionary<string, SocketClient>();

        /// <summary>
        ///     Returns if a client has passed the authentication stage and thus can receive data.
        /// </summary>
        /// <param name="connectionId">The connection ID of the client</param>
        /// <returns>true or false depending on the authentication state of the client</returns>
        public bool IsClientAuthenticated(string connectionId)
        {
            return _connectedClients.TryGetValue(connectionId, out var client) && client.Authenticated;
        }

        /// <summary>
        ///     Returns if a client is Broadcast enabled. A broadcast enabled client will receive all the service broadcasts
        ///     about status changes.
        /// </summary>
        /// <param name="connectionId">The connection ID of the client</param>
        /// <returns></returns>
        public bool IsClientBroadcastEnabled(string connectionId)
        {
            return !_connectedClients.TryGetValue(connectionId, out var client) || client.BroadcastsEnabled;
        }

        /// <summary>
        ///     Removes a client from the Client List when the client disconnects from the server.
        /// </summary>
        /// <param name="connectionId">The connection ID of the disconnecting client</param>
        public void RemoveClientOnDisconnect(string connectionId)
        {
            _connectedClients.TryRemove(connectionId, out _);
        }

        /// <summary>
        ///     Adds a client to the Client List when the client connects to the server. In case a client
        ///     already exists with the specified connectionId then the old client entry is removed before the adding
        ///     the new one.
        /// </summary>
        /// <param name="connectionId">The connection ID of the connecting client</param>
        public void AddClientOnConnect(string connectionId)
        {
            // Remove existing client if present
            _connectedClients.TryRemove(connectionId, out _);

            var client = new SocketClient(connectionId);
            _connectedClients.TryAdd(connectionId, client);
        }

        /// <summary>
        ///     Given a connection ID the function returns a SocketClient object.
        /// </summary>
        /// <param name="connectionId">The connection ID.</param>
        /// <returns>A SocketClient object or null</returns>
        public SocketClient Client(string connectionId)
        {
            return _connectedClients.TryGetValue(connectionId, out var client) ? client : null;
        }
    }
}
