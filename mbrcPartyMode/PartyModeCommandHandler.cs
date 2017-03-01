using MbrcPartyMode.Helper;

namespace MbrcPartyMode
{
    public enum MappingCommand
    {
        StartPlayer,
        StopPlayer,
        SkipForward,
        SkipBackward,
        AddToPlayList,
        DeleteFromPLayList,
        ClientConnected,
        ClientDisconnected,
        ServerStart,
        ServerStop,
        StopServer,
        CanSetVolume,
        CommandNotImplemented
    }

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

        public bool IsCommandAllowed(MappingCommand cmd, ClientAdress adr)
        {
            if (adr == null) return true;

            if (cmd == MappingCommand.CommandNotImplemented) return true;

            switch (cmd)
            {
                case MappingCommand.StartPlayer:
                case MappingCommand.StopPlayer:
                    return adr.CanStartStopPlayer;
                case MappingCommand.AddToPlayList:
                    return adr.CanAddToPlayList;
                case MappingCommand.DeleteFromPLayList:
                    return adr.CanDeleteFromPlayList;
                case MappingCommand.SkipBackward:
                    return adr.CanSkipBackwards;
                case MappingCommand.SkipForward:
                    return adr.CanSkipForwards;
                case MappingCommand.CanSetVolume:
                    return adr.CanVolumeUpDown;
                case MappingCommand.CommandNotImplemented:
                    return true;
                default:
                    return true;
            }
        }

        public void OnClientConnected(ConnectedClientAddress adr)
        {
            ClientConnected?.Invoke(this, new ClientEventArgs(adr));
        }

        public void OnClientDisconnected(ConnectedClientAddress adr)
        {
            ClientDisconnected?.Invoke(this, new ClientEventArgs(adr));
        }

        public void OnServerCommandExecuted(string client, string command, bool isCmdAllowed)
        {
            ServerCommandExecuted?.Invoke(this, new ServerCommandEventArgs(client, command, isCmdAllowed));
        }
    }
}