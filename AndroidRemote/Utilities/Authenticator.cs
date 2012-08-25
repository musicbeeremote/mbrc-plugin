using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Utilities
{
    /// <summary>
    /// Responsible for the client authentication. Keeps a list of the connected clients.
    /// </summary>
    public static class Authenticator
    {
        private static readonly List<SocketClient> SocketClients = new List<SocketClient>();
        /// <summary>
        /// Returns if a clients has passed the authentication stage and thus can receive data.
        /// </summary>
        /// <param name="clientId">Represents the id of client</param>
        /// <returns>true or false depending on the authentication state of the client</returns>
        public static bool IsClientAuthenticated(int clientId)
        {
            return
                (from socketClient in SocketClients
                 where socketClient.ClientId == clientId
                 select socketClient.Authenticated).FirstOrDefault();
        }

        /// <summary>
        ///  Removes a client from the Client List when the client disconnects from the server.
        /// </summary>
        /// <param name="e"></param>
        public static void RemoveClientOnDisconnect(MessageEventArgs e)
        {
            foreach (SocketClient client in SocketClients)
            {
                if (client.ClientId != e.ClientId) continue;
                SocketClients.Remove(client);
                break;
            }
        }

        /// <summary>
        /// Adds a client to the Client List when the client connects to the server. In case a client
        /// already exists with the specified id then the old client entry is removed before the adding
        /// the new one.
        /// </summary>
        /// <param name="e"></param>
        public static void AddClientOnConnect(MessageEventArgs e)
        {
            foreach (SocketClient client in SocketClients)
            {
                if (client.ClientId != e.ClientId) continue;
                SocketClients.Remove(client);
                break;
            }

            SocketClient newClient = new SocketClient(e.ClientId);
            SocketClients.Add(newClient);
        }

        /// <summary>
        /// Given a client id the function returns a SocketClient object.
        /// </summary>
        /// <param name="id">The client id.</param>
        /// <returns>A SocketClient object.</returns>
        public static SocketClient Client(int id)
        {
            return SocketClients.FirstOrDefault(socketClient => socketClient.ClientId == id);
        }
    }
}
