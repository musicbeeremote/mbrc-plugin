using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MusicBeePlugin.Tools
{
    public class NetworkTools
    {
        public static List<string> GetPrivateAddressList()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            return (from address in host.AddressList where address.AddressFamily == AddressFamily.InterNetwork select address.ToString()).ToList();

        }

        public static List<IPAddress> GetAddressList()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            return (from address in host.AddressList where address.AddressFamily == AddressFamily.InterNetwork select address).ToList();

        }

        public static IPAddress GetSubnetMask(string ipaddress)
        {
            IPAddress address = IPAddress.Parse(ipaddress);
            foreach (UnicastIPAddressInformation information in from adapter in NetworkInterface.GetAllNetworkInterfaces() from information in adapter.GetIPProperties().UnicastAddresses where information.Address.AddressFamily == AddressFamily.InterNetwork where address.Equals(information.Address) select information)
            {
                return information.IPv4Mask;
            }
            throw new ArgumentException(string.Format("unable to find subnet mask for '{0}'", address));
        }

        public static IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask)
        {
            byte[] addressBytes = address.GetAddressBytes();
            byte[] maskBytes = subnetMask.GetAddressBytes();

            if (addressBytes.Length != maskBytes.Length)
            {
                throw new ArgumentException("ip and mask lengths don't match");
            } 
            
            byte[] broadcastBytes = new byte[addressBytes.Length];
            for (int i = 0; i < broadcastBytes.Length; i++)
            {
                broadcastBytes[i] = (byte) (addressBytes[i] & maskBytes[i]);
            }
            return new IPAddress(broadcastBytes);
        }
    }
}
