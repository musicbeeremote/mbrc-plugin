using System.Net;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Commands.Logs;
using MusicBeeRemote.Core.Events;
using TinyMessenger;

namespace MusicBeeRemote.Core.Network
{
    public class ClientManager
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ClientRepository _repository;
        private readonly LogRepository _logRepository;

        public bool PermissionMode { get; set; } = false;

        public ClientManager(ITinyMessengerHub hub, ClientRepository repository, LogRepository logRepository)
        {
            _hub = hub;
            _repository = repository;
            _logRepository = logRepository;
            _hub.Subscribe<ConnectionReadyEvent>(msg => OnClientConnected(msg.Client));
            _hub.Subscribe<ConnectionRemovedEvent>(msg => OnClientDisconnected(msg.Client));
        }

        private void OnClientDisconnected(SocketConnection connection)
        {
            _repository.ReduceConnections(connection.ClientId);
            if (!connection.BroadcastsEnabled)
            {
                return;
            }

            var log = new ExecutionLog
            {
                Client = connection.ClientId,
                Command = "Disconnected"
            };

            _repository.ResetClientConnections(connection.ClientId);

            _hub.Publish(new ActionLoggedEvent(log));
            _logRepository.InsertLog(log);
        }

        private void OnClientConnected(SocketConnection connection)
        {
            var client = CreateClient(connection.IpAddress, connection.ClientId);
            _repository.InsertClient(client);

            if (!connection.BroadcastsEnabled)
            {
                return;
            }
            _hub.Publish(new ClientDataUpdateEvent(client));
            Log("Connected", ExecutionStatus.Executed, connection.ClientId);
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


        public CommandPermissions ClientPermissions(string clientId)
        {
            var client = _repository.GetClientById(clientId);
            return client?.ClientPermissions ?? CommandPermissions.None;
        }

        public void Log(string name, ExecutionStatus status, string clientId)
        {
            var logEntry = new ExecutionLog
            {
                Client = clientId,
                Command = name,
                Status = status
            };
            _logRepository.InsertLog(logEntry);
            _hub.Publish(new ActionLoggedEvent(logEntry));
        }

        public class ActionLoggedEvent : ITinyMessage
        {
            public object Sender { get; } = null;

            public ExecutionLog Log { get; }

            public ActionLoggedEvent(ExecutionLog log)
            {
                Log = log;
            }
        }

        public class ClientDataUpdateEvent : ITinyMessage
        {
            public RemoteClient Client { get; }

            public object Sender { get; } = null;

            public ClientDataUpdateEvent(RemoteClient client)
            {
                Client = client;
            }
        }
    }
}