using System;
using System.Collections.Concurrent;

namespace MusicBeePlugin.AndroidRemote.Utilities
{
    using System.Collections.Generic;
    using System.Linq;
    using Networking;

    /// <summary>
    /// Responsible for the client authentication. Keeps a list of the connected clients.
    /// </summary>
    public static class Authenticator
    {
        private static readonly ConcurrentDictionary<string, SocketClient> ConnectedClients =
            new ConcurrentDictionary<string, SocketClient>();

        /// <summary>
        /// Returns if a clients has passed the authentication stage and thus can receive data.
        /// </summary>
        /// <param name="clientId">Represents the clientId of client</param>
        /// <returns>true or false depending on the authentication state of the client</returns>
        public static bool IsClientAuthenticated(string clientId)
        {
            bool authenticated = false;
            SocketClient client;
            if (ConnectedClients.TryGetValue(clientId, out client))
            {
                authenticated = client.Authenticated;
            }
            return authenticated;
        }

        /// <summary>
        /// Returns if a client is Broadcast enabled. A broadcast enabled client will receive all the service broadcasts
        /// about status changes.
        /// </summary>
        /// <param name="clientId">The id of the client that is used as an identification</param>
        /// <returns></returns>
        public static bool IsClientBroadcastEnabled(string clientId)
        {
            var enabled = true;
            SocketClient client;
            if (ConnectedClients.TryGetValue(clientId, out client))
            {
                enabled = client.BroadcastsEnabled;
            }
            return enabled;
        }

        /// <summary>
        ///  Removes a client from the Client List when the client disconnects from the server.
        /// </summary>
        /// <param name="clientId"> </param>
        public static void RemoveClientOnDisconnect(string clientId)
        {
            SocketClient client;
            if (ConnectedClients.TryRemove(clientId, out client))
            {
                //?
            }
        }

        /// <summary>
        /// Adds a client to the Client List when the client connects to the server. In case a client
        /// already exists with the specified clientId then the old client entry is removed before the adding
        /// the new one.
        /// </summary>
        /// <param name="clientId"> </param>
        public static void AddClientOnConnect(string clientId)
        {
            SocketClient client;
            if (ConnectedClients.ContainsKey(clientId))
            {
                ConnectedClients.TryRemove(clientId, out client);
            }
            client = new SocketClient(clientId);
            ConnectedClients.TryAdd(clientId, client);
        }

        /// <summary>
        /// Given a client clientId the function returns a SocketClient object.
        /// </summary>
        /// <param name="clientId">The client clientId.</param>
        /// <returns>A SocketClient object. or null</returns>
        public static SocketClient Client(string clientId)
        {
            SocketClient client;
            ConnectedClients.TryGetValue(clientId, out client);
            return client;
        }

        /// <summary>
        /// Checks the client protocol version of a specified client and how it matches
        /// against the server protocol version. If the client doesn't match the 
        /// method will return true.
        /// </summary>
        /// <param name="clientId">The ide of the client</param>
        /// <returns>True if the version is different false if it is the same.</returns>
        public static bool ClientProtocolMisMatch(string clientId)
        {
            var client = Client(clientId);
            var clientProtocolVersion = client?.ClientProtocolVersion
                                        ?? (int) Constants.ProtocolVersion;

            return Math.Abs(clientProtocolVersion - Constants.ProtocolVersion) > 0;
        }

        public static int ClientProtocolVersion(string clientId)
        {
            var client = Client(clientId);
            return client?.ClientProtocolVersion ?? 2;
        }
    }
}