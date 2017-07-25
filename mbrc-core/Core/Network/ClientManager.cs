using System.Net;
using MusicBeeRemote.Core.Events;
using TinyMessenger;

namespace MusicBeeRemote.Core.Network
{
    public class ClientManager
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ClientRepository _repository;

        public ClientManager(ITinyMessengerHub hub, ClientRepository repository)
        {
            _hub = hub;
            _repository = repository;            
            _hub.Subscribe<ConnectionReadyEvent>(msg => OnClientConnected(msg.Client));
            _hub.Subscribe<ConnectionRemovedEvent>(msg => OnClientDisconnected(msg.Client));
        }

        private void OnClientDisconnected(SocketConnection connection)
        {
            
        }

        private void OnClientConnected(SocketConnection connection)
        {
            // A connection where broadcast is disabled is from a secondary connection of an existing client.
            // Each client should only have one active broadcast enabled connection that is the main communication
            // channel.
            if (!connection.BroadcastsEnabled)
            {            
                return;
            }

            var client = CreateClient(connection.IpAddress, connection.ClientId);
            _repository.InsertClient(client);            
        }
        
        private static RemoteClient CreateClient(IPAddress ipadress, string clientId)
        {
            var client = new RemoteClient(Tools.GetMacAddress(ipadress), ipadress)
            {
                ClientId = clientId
            };
            client.AddConnection();
            return client;
        }
    }
}