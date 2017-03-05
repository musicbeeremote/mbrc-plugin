using System;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.PartyMode.Core
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