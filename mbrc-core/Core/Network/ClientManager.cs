using System;
using System.Net;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Commands.Logs;
using MusicBeeRemote.Core.Events.Status.Internal;
using TinyMessenger;

namespace MusicBeeRemote.Core.Network
{
    public class ClientManager
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ClientRepository _repository;
        private readonly LogRepository _logRepository;

        public ClientManager(ITinyMessengerHub hub, ClientRepository repository, LogRepository logRepository)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _repository = repository;
            _logRepository = logRepository;
            _hub.Subscribe<ConnectionReadyEvent>(msg => OnClientConnected(msg.Client));
            _hub.Subscribe<ConnectionRemovedEvent>(msg => OnClientDisconnected(msg.Client));
        }

        public bool PermissionMode { get; set; } = false;

        public CommandPermissions ClientPermissions(string clientId)
        {
            var client = _repository.GetClientById(clientId);
            return client?.ClientPermissions ?? CommandPermissions.None;
        }

        public void Log(string name, ExecutionStatus status, string clientId)
        {
            var logEntry = new ExecutionLog { Client = clientId, Command = name, Status = status };
            _logRepository.InsertLog(logEntry);
            _hub.Publish(new ActionLoggedEvent(logEntry));
        }

        private static RemoteClient CreateClient(IPAddress address, string clientId)
        {
            var client = new RemoteClient(Tools.GetMacAddress(address), address) { ClientId = clientId };
            client.AddConnection();
            return client;
        }

        private void OnClientDisconnected(SocketConnection connection)
        {
            _repository.ReduceConnections(connection.ClientId);
            if (!connection.BroadcastsEnabled)
            {
                return;
            }

            var log = new ExecutionLog { Client = connection.ClientId, Command = "Disconnected" };

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
    }
}
