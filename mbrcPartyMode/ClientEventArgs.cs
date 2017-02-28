using System;
using mbrcPartyMode.Helper;

namespace mbrcPartyMode
{
    public class ClientEventArgs : EventArgs
    {
        public ClientEventArgs(ConnectedClientAddress adr)
        {
            Adr = adr;
        }

        public ConnectedClientAddress Adr { get; set; }
    }

}