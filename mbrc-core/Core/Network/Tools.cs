using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using MusicBeeRemote.Properties;
using NLog;

namespace MusicBeeRemote.Core.Network
{
    public static class Tools
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

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

        public static IPAddress GetSubnetMask(string ipAddress)
        {
            var address = IPAddress.Parse(ipAddress);

            var unicastIpAddressInformation = from adapter in NetworkInterface.GetAllNetworkInterfaces()
                from information in adapter.GetIPProperties().UnicastAddresses
                where information.Address.AddressFamily == AddressFamily.InterNetwork
                where address.Equals(information.Address)
                select information;

            return unicastIpAddressInformation.First()?.IPv4Mask ?? throw new ArgumentException($"unable to find subnet mask for '{address}'");
        }

        public static IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            if (subnetMask == null)
            {
                throw new ArgumentNullException(nameof(subnetMask));
            }

            var addressBytes = address.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();

            if (addressBytes.Length != maskBytes.Length)
            {
                throw new ArgumentException(Resources.ExceptionAddressInvalidLength);
            }

            var broadcastBytes = new byte[addressBytes.Length];
            for (var i = 0; i < broadcastBytes.Length; i++)
            {
                broadcastBytes[i] = (byte)(addressBytes[i] & maskBytes[i]);
            }

            return new IPAddress(broadcastBytes);
        }

        public static PhysicalAddress GetMacAddress(IPAddress ipAddress)
        {
            if (ipAddress == null)
            {
                return null;
            }

            try
            {
                var bpMacAddr = new byte[6];
                var len = bpMacAddr.Length;
                SendARP(ipAddress.GetHashCode(), 0, bpMacAddr, ref len);
                var adr = new PhysicalAddress(bpMacAddr);
                return adr;
            }
            catch (Exception e)
            {
                _logger.Debug($"Get MAC address failed IP: {ipAddress}\n{e.Message} \n{e.StackTrace}");
                throw;
            }
        }

        [DllImport("iphlpapi.dll")]
        private static extern long SendARP(int destIp, int scrIp, [Out] byte[] pMacAddr, ref int phyAddr);
    }
}
