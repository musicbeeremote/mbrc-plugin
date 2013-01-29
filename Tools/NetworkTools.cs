using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MusicBeePlugin.Tools
{
    class NetworkTools
    {
        public static List<string> GetPrivateAddressList()
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());

            return (from address in host.AddressList where address.AddressFamily == AddressFamily.InterNetwork select address.ToString()).ToList();

        }
    }
}
