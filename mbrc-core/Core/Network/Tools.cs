using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MusicBeeRemote.Core.Network
{
    public class Tools
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
            foreach (var information in
                from adapter in NetworkInterface.GetAllNetworkInterfaces()
                from information in adapter.GetIPProperties().UnicastAddresses
                where information.Address.AddressFamily == AddressFamily.InterNetwork
                where address.Equals(information.Address)
                select information)
            {
                return information.IPv4Mask;
            }
            throw new ArgumentException($"unable to find subnet mask for '{address}'");
        }

        public static IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask)
        {
            var addressBytes = address.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();

            if (addressBytes.Length != maskBytes.Length)
            {
                throw new ArgumentException("ip and mask lengths don't match");
            }

            var broadcastBytes = new byte[addressBytes.Length];
            for (var i = 0; i < broadcastBytes.Length; i++)
            {
                broadcastBytes[i] = (byte) (addressBytes[i] & maskBytes[i]);
            }
            return new IPAddress(broadcastBytes);
        }

        [DllImport("iphlpapi.dll")]
        private static extern long SendARP(int destIp, int scrIp, [Out] byte[] pMacAddr, ref int phyAddr);

        public static PhysicalAddress GetMacAddress(IPAddress ipAddress)
        {
            if (ipAddress == null) return null;
            try
            {
                var bpMacAddr = new byte[6];
                var len = bpMacAddr.Length;
                var res = SendARP(ipAddress.GetHashCode(), 0, bpMacAddr, ref len);
                var adr = new PhysicalAddress(bpMacAddr);
                return adr;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Get MAC address failed IP: " + ipAddress + "\n" + e.Message + " \n" + e.StackTrace);
                throw e;
            }
        }
    }
}