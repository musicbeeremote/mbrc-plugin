using System;
using MusicBeeRemote.Core.Network;

namespace MusicBeeRemote.PartyMode.Core
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