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

        private static PartyModeModel instance;
        private Settings settings;
        private List<ConnectedClientAddress> connectedAdresses;
        private SettingsHandler handler;
        private CycledList<ServerMessage> serverMessages;

        #endregion vars

        #region constructor

        public PartyModeModel()
        {
            handler = new SettingsHandler();
            settings = handler.GetSettings();

            PartyModeCommandHandler commandHandler = PartyModeCommandHandler.Instance;

            commandHandler.ClientConnected += ClientConnected;
            commandHandler.ClientDisconnected += ClientDisconnected;
            commandHandler.ServerCommandExecuted += ServerCommandExecuted;
            connectedAdresses = new List<ConnectedClientAddress>();

            serverMessages = new CycledList<ServerMessage>(10000);

            ServerMessagesQueue = new ConcurrentQueue<ServerMessage>();
        }

        #endregion constructor

        public static PartyModeModel Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new PartyModeModel();
                }
                return instance;
            }
        }

        public void AddAddress(ConnectedClientAddress adr)
        {
            connectedAdresses.Add(adr);
        }

        public void RemoveAddress(ConnectedClientAddress adr)
        {
            if (connectedAdresses.Any())
            {
                var cadr = connectedAdresses.SingleOrDefault(x => x.ClientId == adr.ClientId);
                if (cadr != null)
                {
                    connectedAdresses.Remove(adr);
                }
            }
        }

        public ConnectedClientAddress GetConnectedClientAdresss(string clientId, IPAddress ipadress)
        {
            if (connectedAdresses.Any())
            {
                var cadr = connectedAdresses.SingleOrDefault(x => x.ClientId == clientId);
                if (cadr != null)
                {
                    return cadr;
                }
            }
            return new ConnectedClientAddress(ipadress, clientId);
        }

        public Settings Settings
        {
            get
            {
                return settings;
            }
        }

        public List<ConnectedClientAddress> ConnectedAddresses
        {
            get
            {
                return connectedAdresses;
            }
        }

        private void ClientConnected(object sender, ClientEventArgs e)
        {
            if (e.adr != null)
            {
                if (!connectedAdresses.Any(x => x.MacAdress.ToString() == e.adr.MacAdress.ToString()))
                {
                    //to do: check if the Macadr is not null
                    ClientAdress knownAdress = settings.KnownAdresses.SingleOrDefault(x => x.MacAdress.ToString() == e.adr.MacAdress.ToString());
                    if (knownAdress != null)
                    {
                        knownAdress.IpAddress = e.adr.IpAddress;
                        knownAdress.LastLogIn = DateTime.Now;
                        connectedAdresses.Add(new ConnectedClientAddress(knownAdress, e.adr.ClientId));
                    }
                    else
                    {
                        connectedAdresses.Add(e.adr);
                    }
                }
                OnPropertyChanged(nameof(ConnectedAddresses));
            }
        }

        private void ClientDisconnected(object sender, ClientEventArgs e)
        {
            if (connectedAdresses.Contains(e.adr))
            {
                connectedAdresses.Remove(e.adr);
            }
        }

        private void ServerCommandExecuted(object sender, ServerCommandEventArgs e)
        {
            
            var serverMessage = new ServerMessage(e.Client, e.Command, !e.IsCommandAllowed);
            serverMessages.Add(serverMessage);
            ServerMessagesQueue.Enqueue(serverMessage);
            OnPropertyChanged(nameof(ServerMessagesQueue));
        }

        public ConcurrentQueue<ServerMessage> ServerMessagesQueue;

        public void SaveSettings()
        {
            foreach (ClientAdress adr in connectedAdresses)
            {
                ClientAdress kadr = settings.KnownAdresses.SingleOrDefault(x => x.MacAdress.ToString() == adr.MacAdress.ToString());
                if (kadr != null)
                {
                    kadr = adr;
                }
                else
                {
                    settings.KnownAdresses.Add(adr);
                }
            }

            handler.SaveSettings(settings);
        }

        public void RequestAllServerMessages()
        {
            this.ServerMessagesQueue= new ConcurrentQueue<ServerMessage>(serverMessages);
            OnPropertyChanged(nameof(ServerMessagesQueue));
        }

    }
}