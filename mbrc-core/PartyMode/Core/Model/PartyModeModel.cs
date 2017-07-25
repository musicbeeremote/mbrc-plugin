using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;
using MusicBeeRemote.PartyMode.Core.Helper;
using MusicBeeRemote.PartyMode.Core.Repository;
using MusicBeeRemote.PartyMode.Core.Tools;
using TinyMessenger;

namespace MusicBeeRemote.PartyMode.Core.Model
{
    public class PartyModeModel : ViewModelBase
    {
        #region vars

        private readonly SettingsHandler _handler;
        private readonly PartyModeRepository _repository;
        private readonly ITinyMessengerHub _hub;

        #endregion vars

        #region properties

        public Settings Settings { get; }
        public BindingList<RemoteClient> KnownClients { get; }

        #endregion

        public PartyModeModel(SettingsHandler handler, PartyModeRepository repository, ITinyMessengerHub _hub)
        {
            this._hub = _hub;
            _handler = handler;
            _repository = repository;
            
            Settings = _handler.GetSettings();

            // Load stored
            KnownClients = new BindingList<RemoteClient>(_repository.GetKnownClients());
        }

        public void AddClientIfNotExists(SocketConnection connection)
        {
            var ipadress = connection.IpAddress;
            var clientId = connection.ClientId;

            if (!KnownClients.Any())
            {
                NewClient(ipadress, clientId);
            }
            else
            {
                var client = KnownClients.SingleOrDefault(x => x.ClientId == clientId);
                if (client != null)
                {
                    client.AddConnection();
                }
                else
                {
                    NewClient(ipadress, clientId);
                }
            }

            LogCommand(clientId, "New connection", true);
            OnPropertyChanged(nameof(KnownClients));
        }

        private void NewClient(IPAddress ipadress, string clientId)
        {
            var remoteClient = CreateClient(ipadress, clientId);
            KnownClients.Add(remoteClient);
            _repository.InsertClient(remoteClient);
        }

        private static RemoteClient CreateClient(IPAddress ipadress, string clientId)
        {
            var client = new RemoteClient(PartyModeNetworkTools.GetMacAddress(ipadress), ipadress)
            {
                ClientId = clientId
            };
            client.AddConnection();
            return client;
        }

        public void LogCommand(string clientId, string command, bool hasPermissions)
        {
            if (command == Constants.Ping || command == Constants.Pong)
            {
                return;
            }

            var status = !hasPermissions ? ExecutionStatus.Denied : ExecutionStatus.Executed;
            var log = new PartyModeLog(clientId, command, status);

            _repository.InsertLog(log);
            _hub.PublishAsync(new CommandProcessedEvent(log));
        }

        public void RemoveConnection(RemoteClient client)
        {
            var savedClient = KnownClients.SingleOrDefault(x => x.ClientId == client.ClientId);
            savedClient?.RemoveConnection();
            LogCommand(client.ClientId, "connection closed", true);
        }

        public RemoteClient GetClient(string clientId)
        {
            return KnownClients.SingleOrDefault(x => x.ClientId == clientId);
        }


        public List<PartyModeLog> GetLogs()
        {
            return _repository.GetLogs();
        }

        public void UpdateClient(RemoteClient client)
        {
            _repository.UpdateClient(client);
        }
    }
}