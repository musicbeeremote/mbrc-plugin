using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MusicBeePlugin.Tools
{
    internal static class NetworkTools
    {
        public static List<string> GetPrivateAddressList()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            return (from address in host.AddressList
                where address.AddressFamily == AddressFamily.InterNetwork
                select address.ToString()).ToList();
        }

        public static List<IPAddress> GetAddressList()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            return (from address in host.AddressList
                where address.AddressFamily == AddressFamily.InterNetwork
                select address).ToList();
        }

        public static IPAddress GetSubnetMask(string ipaddress)
        {
            var address = IPAddress.Parse(ipaddress);
            foreach (var information in from adapter in NetworkInterface.GetAllNetworkInterfaces()
                     from information in adapter.GetIPProperties().UnicastAddresses
                     where information.Address.AddressFamily == AddressFamily.InterNetwork
                     where address.Equals(information.Address)
                     select information) return information.IPv4Mask;
            throw new ArgumentException(string.Format("unable to find subnet mask for '{0}'", address));
        }

        public static IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask)
        {
            var addressBytes = address.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();

            if (addressBytes.Length != maskBytes.Length) throw new ArgumentException("ip and mask lengths don't match");

            var broadcastBytes = new byte[addressBytes.Length];
            for (var i = 0; i < broadcastBytes.Length; i++) broadcastBytes[i] = (byte)(addressBytes[i] & maskBytes[i]);
            return new IPAddress(broadcastBytes);
        }
    }
}