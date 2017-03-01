using System;
using System.Collections.Concurrent;

namespace MusicBeePlugin.AndroidRemote.Utilities
{
    using Networking;

    /// <summary>
    /// Responsible for the client authentication. Keeps a list of the connected clients.
    /// </summary>
    public static class Authenticator
    {
        private static readonly ConcurrentDictionary<string, SocketConnection> ActiveConnections =
            new ConcurrentDictionary<string, SocketConnection>();

        /// <summary>
        /// Returns if a clients has passed the authentication stage and thus can receive data.
        /// </summary>
        /// <param name="clientId">Represents the connectionId of client</param>
        /// <returns>true or false depending on the authentication state of the client</returns>
        public static bool IsClientAuthenticated(string clientId)
        {
            var authenticated = false;
            SocketConnection connection;
            if (ActiveConnections.TryGetValue(clientId, out connection))
            {
                authenticated = connection.Authenticated;
            }
            return authenticated;
        }

        /// <summary>
        /// Returns if a client is Broadcast enabled. A broadcast enabled client will receive all the service broadcasts
        /// about status changes.
        /// </summary>
        /// <param name="connectionId">The id of the client that is used as an identification</param>
        /// <returns></returns>
        public static bool IsClientBroadcastEnabled(string connectionId)
        {
            var enabled = true;
            SocketConnection connection;
            if (ActiveConnections.TryGetValue(connectionId, out connection))
            {
                enabled = connection.BroadcastsEnabled;
            }
            return enabled;
        }

        /// <summary>
        ///  Removes a client from the Connection List when the client disconnects from the server.
        /// </summary>
        /// <param name="connectionId"> </param>
        public static void RemoveClientOnDisconnect(string connectionId)
        {
            SocketConnection connection;
            if (ActiveConnections.TryRemove(connectionId, out connection))
            {
                //?
            }
        }

        /// <summary>
        /// Adds a client to the Connection List when the client connects to the server. In case a client
        /// already exists with the specified connectionId then the old client entry is removed before the adding
        /// the new one.
        /// </summary>
        /// <param name="connectionId"> </param>
        public static void AddClientOnConnect(string connectionId)
        {
            SocketConnection connection;
            if (ActiveConnections.ContainsKey(connectionId))
            {
                ActiveConnections.TryRemove(connectionId, out connection);
            }
            connection = new SocketConnection(connectionId);
            ActiveConnections.TryAdd(connectionId, connection);
        }

        /// <summary>
        /// Given a client connectionId the function returns a SocketConnection object.
        /// </summary>
        /// <param name="connectionId">The client connectionId.</param>
        /// <returns>A SocketConnection object. or null</returns>
        public static SocketConnection GetConnection(string connectionId)
        {
            SocketConnection connection;
            ActiveConnections.TryGetValue(connectionId, out connection);
            return connection;
        }

        /// <summary>
        /// Checks the client protocol version of a specified client and how it matches
        /// against the server protocol version. If the client doesn't match the 
        /// method will return true.
        /// </summary>
        /// <param name="connectionId">The ide of the client</param>
        /// <returns>True if the version is different false if it is the same.</returns>
        public static bool ClientProtocolMisMatch(string connectionId)
        {
            var connection = GetConnection(connectionId);
            var clientProtocolVersion = connection?.ClientProtocolVersion
                                        ?? Constants.ProtocolVersion;

            return Math.Abs(clientProtocolVersion - Constants.ProtocolVersion) > 0;
        }

        public static int ClientProtocolVersion(string connectionId)
        {
            var connection = GetConnection(connectionId);
            return connection?.ClientProtocolVersion ?? 2;
        }

        public static string ClientId(string connectionId)
        {
            var connection = GetConnection(connectionId);
            return connection?.ClientId ?? string.Empty;
        }
    }
}