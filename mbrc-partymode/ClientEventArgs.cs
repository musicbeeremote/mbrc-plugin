using System;
using MbrcPartyMode.Helper;

namespace MbrcPartyMode
{
    public class ClientEventArgs : EventArgs
    {
        public ClientEventArgs(ConnectedClientAddress address)
        {
            Address = address;
        }

        public ConnectedClientAddress Address { get; set; }
    }

}