using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.Events;
using MusicBeePlugin.Networking;

namespace MusicBeePlugin.Utilities
{
    public static class Authenticator
    {
        private static readonly List<SocketClient> SocketClients = new List<SocketClient>();
        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientId"></param>
        /// <returns></returns>
        public static bool IsClientAuthenticated(int clientId)
        {
            return
                (from socketClient in SocketClients
                 where socketClient.ClientId == clientId
                 select socketClient.Authenticated).FirstOrDefault();
        }

        public static void RemoveClientOnDisconnect(MessageEventArgs e)
        {
            foreach (SocketClient client in SocketClients)
            {
                if (client.ClientId != e.ClientId) continue;
                SocketClients.Remove(client);
                break;
            }
        }

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
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static SocketClient Client(int id)
        {
            return SocketClients.FirstOrDefault(socketClient => socketClient.ClientId == id);
        }
    }
}
