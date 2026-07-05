using System.Collections.Generic;
using System.Net;

namespace MusicBeePlugin.Utilities.Network
{
    public interface INetworkTools
    {
        List<IPAddress> GetAddressList();
        List<string> GetPrivateAddressList();
        IPAddress GetSubnetMask(string address);
        IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask);
    }
}
