using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.PartyMode.Core.Helper;
using MusicBeePlugin.PartyMode.Core.ViewModel;
using static MusicBeePlugin.PartyMode.Core.Tools.PartyModeNetworkTools;

namespace MusicBeePlugin.PartyMode.Core.Model
{
    public class PartyModeModel : ModelBase
    {
        #region vars

        private readonly SettingsHandler _handler;

        private static PartyModeModel _instance;
        private readonly CycledList<PartyModeLogs> _logs;

        #endregion vars

        public PartyModeModel()
        {
            _handler = new SettingsHandler();
            Settings = _handler.GetSettings();

            var commandHandler = PartyModeCommandHandler.Instance;

            commandHandler.ClientConnected += ClientConnected;
            commandHandler.ClientDisconnected += ClientDisconnected;
            commandHandler.ServerCommandExecuted += ServerCommandExecuted;
            KnownAddresses = new List<RemoteClient>();

            _logs = new CycledList<PartyModeLogs>(10000);

            ServerMessagesQueue = new ConcurrentQueue<PartyModeLogs>();
        }

        #region constructor

        #endregion constructor

        public static PartyModeModel Instance => _instance ?? (_instance = new PartyModeModel());

        public RemoteClient GetClientAddress(string clientId, IPAddress ipadress)
        {
            if (KnownAddresses.Any())
            {
                var cadr = KnownAddresses.SingleOrDefault(x => x.ClientId == clientId);
                if (cadr != null)
                {
                    return cadr;
                }
            }

            var macAddress = GetMacAddress(ipadress);
            var client = new RemoteClient(macAddress, ipadress) {ClientId = clientId};
            return client;
        }

        public Settings Settings { get; }

        public List<RemoteClient> KnownAddresses { get; }

        private void ClientConnected(object sender, ClientEventArgs e)
        {
            if (e.Client == null) return;
            if (KnownAddresses.All(x => x.MacAdress.ToString() != e.Client.MacAdress.ToString()))
            {
                //to do: check if the Macadr is not null
                var client = Settings.KnownAdresses.SingleOrDefault(x => Equals(x.MacAdress, e.Client.MacAdress));
                if (client != null)
                {
                    client.IpAddress = e.Client.IpAddress;
                    client.LastLogIn = DateTime.Now;
                    KnownAddresses.Add(client);
                }
                else
                {
                    KnownAddresses.Add(e.Client);
                }
            }
            OnPropertyChanged(nameof(KnownAddresses));
        }

        private void ClientDisconnected(object sender, ClientEventArgs e)
        {
            if (KnownAddresses.Contains(e.Client))
            {
      
            }
        }

        private void ServerCommandExecuted(object sender, ServerCommandEventArgs e)
        {
            if (e.Command == Constants.Ping || e.Command == Constants.Pong)
            {
                return;
            }

            var logMessages = new PartyModeLogs(e.Client, e.Command, !e.IsCommandAllowed);
            _logs.Add(logMessages);
            ServerMessagesQueue.Enqueue(logMessages);
            OnPropertyChanged(nameof(ServerMessagesQueue));
        }

        public ConcurrentQueue<PartyModeLogs> ServerMessagesQueue;

        public void SaveSettings()
        {
            foreach (var adr in KnownAddresses)
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
            ServerMessagesQueue = new ConcurrentQueue<PartyModeLogs>(_logs);
            OnPropertyChanged(nameof(ServerMessagesQueue));
        }
    }
}