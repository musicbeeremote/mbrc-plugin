using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    class ServiceDiscovery
    {
        private const int UdpPort = 45345;
        private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.1.10");

        public void Start()
        {
            UdpClient listener = new UdpClient(UdpPort);
            IPEndPoint mCastGroupEP = new IPEndPoint(MulticastAddress,UdpPort);

            try
            {
                listener.JoinMulticastGroup(MulticastAddress);
                while (true)
                {
                    byte[] bytes = listener.Receive(ref mCastGroupEP);
                    listener.EnableBroadcast = true;
                    Debug.WriteLine(Encoding.UTF8.GetString(bytes));
                    byte[] data = Encoding.UTF8.GetBytes("testtae");
          
                    listener.Send(data, data.Length);

                }
            }
            catch (Exception)
            {
                
                throw;
            }
        }
      
    }
}
