using MusicBeePlugin.AndroidRemote.Commands;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.PartyMode.Core
{
    public delegate void ClientConnectedEventHandler(object sender, ClientEventArgs e);

    public delegate void ClientDisconnectedEventHandler(object sender, ClientEventArgs e);

    public delegate void ServerCommandExecutedEventHandler(object sender, ServerCommandEventArgs e);

    public class PartyModeCommandHandler
    {
        #region vars

        private static PartyModeCommandHandler _instance;

        public event ClientConnectedEventHandler ClientConnected;
        public event ClientDisconnectedEventHandler ClientDisconnected;
        public event ServerCommandExecutedEventHandler ServerCommandExecuted;

        #endregion vars

        private PartyModeCommandHandler()
        {
        }


        public static PartyModeCommandHandler Instance => _instance ?? (_instance = new PartyModeCommandHandler());

        public bool ClientCanOnlyAdd(RemoteClient client)
        {
            return client.HasPermission(CommandPermissions.AddTrack | ~CommandPermissions.StartPlayback);
        }

        public void OnClientConnected(RemoteClient client)
        {
            ClientConnected?.Invoke(this, new ClientEventArgs(client));
        }

        public void OnClientDisconnected(RemoteClient client)
        {
            ClientDisconnected?.Invoke(this, new ClientEventArgs(client));
        }

        public void OnServerCommandExecuted(string client, string command, bool isCmdAllowed)
        {
            ServerCommandExecuted?.Invoke(this, new ServerCommandEventArgs(client, command, isCmdAllowed));
        }
    }
}