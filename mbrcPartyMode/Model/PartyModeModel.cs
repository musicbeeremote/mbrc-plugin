using mbrcPartyMode.Helper;
using mbrcPartyMode.ViewModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace mbrcPartyMode.Model
{
    public class PartyModeModel : ModelBase
    {
        #region vars

        private static PartyModeModel _instance;
        private readonly SettingsHandler _handler;
        private readonly CycledList<ServerMessage> _serverMessages;

        #endregion vars

        #region constructor

        public PartyModeModel()
        {
            _handler = new SettingsHandler();
            Settings = _handler.GetSettings();

            var commandHandler = PartyModeCommandHandler.Instance;

            commandHandler.ClientConnected += ClientConnected;
            commandHandler.ClientDisconnected += ClientDisconnected;
            commandHandler.ServerCommandExecuted += ServerCommandExecuted;
            ConnectedAddresses = new List<ConnectedClientAddress>();

            _serverMessages = new CycledList<ServerMessage>(10000);

            ServerMessagesQueue = new ConcurrentQueue<ServerMessage>();
        }

        #endregion constructor

        public static PartyModeModel Instance => _instance ?? (_instance = new PartyModeModel());

        public void AddAddress(ConnectedClientAddress adr)
        {
            ConnectedAddresses.Add(adr);
        }

        public void RemoveAddress(ConnectedClientAddress adr)
        {
            if (!ConnectedAddresses.Any()) return;
            var cadr = ConnectedAddresses.SingleOrDefault(x => x.ClientId == adr.ClientId);
            if (cadr != null)
            {
                ConnectedAddresses.Remove(adr);
            }
        }

        public ConnectedClientAddress GetConnectedClientAdresss(string clientId, IPAddress ipadress)
        {
            if (ConnectedAddresses.Any())
            {
                var cadr = ConnectedAddresses.SingleOrDefault(x => x.ClientId == clientId);
                if (cadr != null)
                {
                    return cadr;
                }
            }
            return new ConnectedClientAddress(ipadress, clientId);
        }

        public Settings Settings { get; }

        public List<ConnectedClientAddress> ConnectedAddresses { get; }

        private void ClientConnected(object sender, ClientEventArgs e)
        {
            if (e.Adr == null) return;
            if (ConnectedAddresses.All(x => x.MacAdress.ToString() != e.Adr.MacAdress.ToString()))
            {
                //to do: check if the Macadr is not null
                var knownAdress = Settings.KnownAdresses.SingleOrDefault(x => x.MacAdress.ToString() == e.Adr.MacAdress.ToString());
                if (knownAdress != null)
                {
                    knownAdress.IpAddress = e.Adr.IpAddress;
                    knownAdress.LastLogIn = DateTime.Now;
                    ConnectedAddresses.Add(new ConnectedClientAddress(knownAdress, e.Adr.ClientId));
                }
                else
                {
                    ConnectedAddresses.Add(e.Adr);
                }
            }
            OnPropertyChanged(nameof(ConnectedAddresses));
        }

        private void ClientDisconnected(object sender, ClientEventArgs e)
        {
            if (ConnectedAddresses.Contains(e.Adr))
            {
                ConnectedAddresses.Remove(e.Adr);
            }
        }

        private void ServerCommandExecuted(object sender, ServerCommandEventArgs e)
        {
            
            var serverMessage = new ServerMessage(e.Client, e.Command, !e.IsCommandAllowed);
            _serverMessages.Add(serverMessage);
            ServerMessagesQueue.Enqueue(serverMessage);
            OnPropertyChanged(nameof(ServerMessagesQueue));
        }

        public ConcurrentQueue<ServerMessage> ServerMessagesQueue;

        public void SaveSettings()
        {
            foreach (var adr in ConnectedAddresses)
            {
                var kadr = Settings.KnownAdresses
                    .SingleOrDefault(x => x.MacAdress.ToString() == adr.MacAdress.ToString());
                if (kadr == null)
                {
                    Settings.KnownAdresses.Add(adr);
                }
            }

            _handler.SaveSettings(Settings);
        }

        public void RequestAllServerMessages()
        {
            ServerMessagesQueue= new ConcurrentQueue<ServerMessage>(_serverMessages);
            OnPropertyChanged(nameof(ServerMessagesQueue));
        }

    }
}