using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace MusicBeePlugin.PartyMode.Core.Tools
{
    public class PartyModeNetworkTools
    {
        [DllImport("iphlpapi.dll")]
        private static extern long SendARP(int destIp, int scrIp, [Out] byte[] pMacAddr, ref int phyAddr);

        public static PhysicalAddress GetMacAddress(IPAddress ipadress)
        {
            if (ipadress == null) return null;
            try
            {
                var bpMacAddr = new byte[6];
                var len = bpMacAddr.Length;
                var res = SendARP(ipadress.GetHashCode(), 0, bpMacAddr, ref len);
                var adr = new PhysicalAddress(bpMacAddr);
                return adr;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Get MAC address failed IP: " + ipadress.Address + "\n" + e.Message + " \n" + e.StackTrace);
                throw e;
            }
        }
    }
}