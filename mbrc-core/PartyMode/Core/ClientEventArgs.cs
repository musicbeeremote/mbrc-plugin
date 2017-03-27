using System;
using MusicBeeRemoteCore.Remote.Networking;

namespace MusicBeeRemoteCore.PartyMode.Core
{
    public class ClientEventArgs : EventArgs
    {
        public ClientEventArgs(RemoteClient client)
        {
            Client = client;
        }

        public RemoteClient Client { get; set; }
    }

}