using System;
using mbrcPartyMode.Helper;

namespace mbrcPartyMode
{
    public class ClientEventArgs : EventArgs
    {
        public ClientEventArgs(ConnectedClientAddress adr)
        {
            this.adr = adr;
        }

        public ConnectedClientAddress adr { get; set; }
    }

}