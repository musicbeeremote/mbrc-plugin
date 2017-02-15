using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace mbrcPartyMode.Tools
{
    public class PartyModeNetworkTools //: NetworkTools
    {
        //[DllImport("wsock32.dll")]
        //private static extern long inet_addr(string s);

        [DllImport("iphlpapi.dll")]
        private static extern long SendARP(int DestIp, int ScrIp, [Out] byte[] pMacAddr, ref int PhyAddr);

        //[DllImport("kernel32.dll")]
        //private static extern long RtlMoveMemory(object dst, object src, long bcount);

        //private const int No_ERROR = 0;

        public static PhysicalAddress GetMACAddress(IPAddress ipadress)
        {
            if (ipadress == null) return null;
            try
            {
                byte[] bpMacAddr = new byte[6];
                int len = bpMacAddr.Length;
                long res = SendARP(ipadress.GetHashCode(), 0, bpMacAddr, ref len);

                PhysicalAddress adr = new PhysicalAddress(bpMacAddr);
                return adr;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Get MAC address failed IP: " + ipadress.Address.ToString() + "\n" + e.Message + " \n" + e.StackTrace);
                throw e;
            }
        }
    } // class
} // namespace