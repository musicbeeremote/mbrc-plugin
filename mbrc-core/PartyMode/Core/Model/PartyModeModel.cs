using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;
using MusicBeeRemote.PartyMode.Core.Helper;
using MusicBeeRemote.PartyMode.Core.Tools;
using NLog;

namespace MusicBeeRemote.PartyMode.Core.Model
{
    public class PartyModeModel : ViewModelBase
    {
        #region vars

        private readonly SettingsHandler _handler;

        public CycledList<PartyModeLogs> Logs { get; }


        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        #endregion vars

        public PartyModeModel(SettingsHandler handler)
        {
            Permissions = Enum.GetValues(typeof(CommandPermissions)).Cast<CommandPermissions>();
            _handler = handler;
            Settings = _handler.GetSettings();

            // Load stored
            KnownClients = new BindingList<RemoteClient>(Settings.KnownClients);

            Logs = new CycledList<PartyModeLogs>(10000);
            ServerMessagesQueue = new ConcurrentQueue<PartyModeLogs>();
        }

        public void AddClientIfNotExists(SocketConnection connection)
        {
            var ipadress = connection.IpAddress;
            var clientId = connection.ClientId;

            if (!KnownClients.Any())
            {
                KnownClients.Add(CreateClient(ipadress, clientId));
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
                    KnownClients.Add(CreateClient(ipadress, clientId));
                }
            }

            LogCommand(new ServerCommandEventArgs(clientId, "New connection", true));

            OnPropertyChanged(nameof(KnownClients));
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

        public Settings Settings { get; }

        public BindingList<RemoteClient> KnownClients { get; }

        public void LogCommand(ServerCommandEventArgs e)
        {
            if (e.Command == Constants.Ping || e.Command == Constants.Pong)
            {
                return;
            }

            var status = !e.IsCommandAllowed ? ExecutionStatus.Denied : ExecutionStatus.Executed;
            var logMessages = new PartyModeLogs(e.Client, e.Command, status);
            Logs.Add(logMessages);
            ServerMessagesQueue.Enqueue(logMessages);
            OnPropertyChanged(nameof(ServerMessagesQueue));
        }

        public ConcurrentQueue<PartyModeLogs> ServerMessagesQueue;
        public IEnumerable<CommandPermissions> Permissions { get; }

        public void SaveSettings()
        {
            Settings.KnownClients = KnownClients.ToList();
            _handler.SaveSettings(Settings);
        }

        public void RequestAllServerMessages()
        {
            ServerMessagesQueue = new ConcurrentQueue<PartyModeLogs>(Logs);
            OnPropertyChanged(nameof(ServerMessagesQueue));
        }

        public void RemoveConnection(RemoteClient client)
        {
            var savedClient = KnownClients.SingleOrDefault(x => x.ClientId == client.ClientId);
            savedClient?.RemoveConnection();
            LogCommand(new ServerCommandEventArgs(client.ClientId, "connection closed", true));
        }

        public RemoteClient GetClient(string clientId)
        {
            return KnownClients.SingleOrDefault(x => x.ClientId == clientId);
        }
    }
}