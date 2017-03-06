using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.PartyMode.Core.Helper;
using MusicBeePlugin.PartyMode.Core.ViewModel;
using NLog;
using static MusicBeePlugin.PartyMode.Core.Tools.PartyModeNetworkTools;

namespace MusicBeePlugin.PartyMode.Core.Model
{
    public class PartyModeModel : ModelBase
    {
        #region vars

        private readonly SettingsHandler _handler;

        private static PartyModeModel _instance;
        private readonly CycledList<PartyModeLogs> _logs;

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        #endregion vars

        public PartyModeModel()
        {
            _handler = new SettingsHandler();
            Settings = _handler.GetSettings();

            var commandHandler = PartyModeCommandHandler.Instance;

            commandHandler.ClientConnected += ClientConnected;
            commandHandler.ClientDisconnected += ClientDisconnected;
            commandHandler.ServerCommandExecuted += ServerCommandExecuted;

            // Load stored
            KnownClients = Settings.KnownClients;

            _logs = new CycledList<PartyModeLogs>(10000);

            ServerMessagesQueue = new ConcurrentQueue<PartyModeLogs>();
        }

        #region constructor

        #endregion constructor

        public static PartyModeModel Instance => _instance ?? (_instance = new PartyModeModel());

        public RemoteClient GetClientAddress(string clientId, IPAddress ipadress)
        {
            if (!KnownClients.Any())
                return new RemoteClient(GetMacAddress(ipadress), ipadress)
                {
                    ClientId = clientId
                };
            var cadr = KnownClients.SingleOrDefault(x => x.ClientId == clientId);
            if (cadr != null)
            {
                return cadr;
            }


            return new RemoteClient(GetMacAddress(ipadress), ipadress)
            {
                ClientId = clientId
            };
        }

        public Settings Settings { get; }

        public List<RemoteClient> KnownClients { get; }

        private void ClientConnected(object sender, ClientEventArgs e)
        {
            // Loopback connection should be excluded
            if (e.Client == null || IPAddress.IsLoopback(e.Client.IpAddress))
            {
                return;
            }

            _logger.Debug($"client connected {e.Client}");

            if (KnownClients.All(x => x.MacAdress.ToString() != e.Client.MacAdress.ToString()))
            {
                //to do: check if the Macadr is not null
                var client = Settings.KnownClients.SingleOrDefault(x => Equals(x.MacAdress, e.Client.MacAdress));
                if (client != null)
                {
                    client.IpAddress = e.Client.IpAddress;
                    client.LastLogIn = DateTime.Now;
                    client.AddConnection();
                    KnownClients.Add(client);
                }
                else
                {
                    e.Client.AddConnection();
                    KnownClients.Add(e.Client);
                }
            }
            OnPropertyChanged(nameof(KnownClients));
        }

        private void ClientDisconnected(object sender, ClientEventArgs e)
        {
            if (!KnownClients.Contains(e.Client)) return;
            var client = Settings.KnownClients
                .SingleOrDefault(x => Equals(x.MacAdress, e.Client.MacAdress));
            if (client != null && client.ActiveConnections > 0)
            {
                client.RemoveConnection();
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
            foreach (var client in KnownClients)
            {
                Settings.KnownClients.Add(client);
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