using System;
using System.Collections.Concurrent;
using System.Net;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Network;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Utilities
{
    /// <summary>
    /// Responsible for the client authentication. Keeps a list of the connected clients.
    /// </summary>
    public class Authenticator
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ITinyMessengerHub _hub;

        private readonly ConcurrentDictionary<string, SocketConnection> _connections =
            new ConcurrentDictionary<string, SocketConnection>();

        public Authenticator(ITinyMessengerHub hub)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _hub.Subscribe<ClientConnectedEvent>(msg => AddConnection(msg.ConnectionId, msg.IpAddress));
            _hub.Subscribe<ClientDisconnectedEvent>(msg => RemoveConnection(msg.ConnectionId));
            _hub.Subscribe<ConnectionReadyEvent>(msg => UpdateConnection(msg.Client));
        }

        /// <summary>
        /// Returns if a clients has passed the authentication stage and thus can receive data.
        /// </summary>
        /// <param name="connectionId">Represents the connectionId of client.</param>
        /// <returns>true or false depending on the authentication state of the client.</returns>
        public bool CanConnectionReceive(string connectionId)
        {
            var authenticated = false;
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                authenticated = connection.Authenticated;
            }

            return authenticated;
        }

        /// <summary>
        /// Returns if a client is Broadcast enabled. A broadcast enabled client will receive all the service broadcasts
        /// about status changes.
        /// </summary>
        /// <param name="connectionId">The id of the client that is used as an identification.</param>
        /// <returns>True if the broadcast functionality is enabled.</returns>
        public bool IsConnectionBroadcastEnabled(string connectionId)
        {
            var enabled = true;
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                enabled = connection.BroadcastsEnabled;
            }

            return enabled;
        }

        /// <summary>
        ///  Removes a client from the Connection List when the client disconnects from the server.
        /// </summary>
        /// <param name="connectionId">The id (unique) of the current connection.</param>
        public void RemoveConnection(string connectionId)
        {
            if (!_connections.ContainsKey(connectionId) || !_connections.TryRemove(connectionId, out var connection))
            {
                return;
            }

            _hub.Publish(new ConnectionRemovedEvent(connection));
            _logger.Debug($"{connectionId} was removed.");
        }

        /// <summary>
        /// Adds a client to the Connection List when the client connects to the server. In case a client
        /// already exists with the specified connectionId then the old client entry is removed before the adding
        /// the new one.
        /// </summary>
        /// <param name="connectionId">The id (unique) of this connection.</param>
        /// <param name="clientAddress">The IP address of the connected client.</param>
        public void AddConnection(string connectionId, IPAddress clientAddress)
        {
            if (_connections.ContainsKey(connectionId))
            {
                _connections.TryRemove(connectionId, out _);
            }

            var connection = new SocketConnection(connectionId) { IpAddress = clientAddress };
            _connections.TryAdd(connectionId, connection);
        }

        /// <summary>
        /// Given a client connectionId the function returns a SocketConnection object.
        /// </summary>
        /// <param name="connectionId">The client connectionId.</param>
        /// <returns>A SocketConnection object. or null.</returns>
        public SocketConnection GetConnection(string connectionId)
        {
            _connections.TryGetValue(connectionId, out var connection);
            return connection;
        }

        /// <summary>
        /// Checks the client protocol version of a specified client and how it matches
        /// against the server protocol version. If the client doesn't match the
        /// method will return true.
        /// </summary>
        /// <param name="connectionId">The ide of the client.</param>
        /// <returns>True if the version is different false if it is the same.</returns>
        public bool ClientProtocolMisMatch(string connectionId)
        {
            var connection = GetConnection(connectionId);
            var clientProtocolVersion = connection?.ClientProtocolVersion
                                        ?? Constants.ProtocolVersion;

            return Math.Abs(clientProtocolVersion - Constants.ProtocolVersion) > 0;
        }

        public int ClientProtocolVersion(string connectionId)
        {
            var connection = GetConnection(connectionId);
            return connection?.ClientProtocolVersion ?? 2;
        }

        public string ClientId(string connectionId)
        {
            var connection = GetConnection(connectionId);
            return connection?.ClientId ?? string.Empty;
        }

        public IPAddress GetIpAddress(string connectionId)
        {
            var connection = GetConnection(connectionId);
            return connection?.IpAddress;
        }

        private void UpdateConnection(SocketConnection connection)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            var connectionId = connection.ConnectionId;
            if (_connections.ContainsKey(connectionId))
            {
                _connections.TryRemove(connectionId, out _);
            }

            _connections.TryAdd(connectionId, connection);
        }
    }
}
