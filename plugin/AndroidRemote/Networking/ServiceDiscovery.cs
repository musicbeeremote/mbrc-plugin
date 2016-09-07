using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.Tools;
using NLog;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    internal class ServiceDiscovery
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private const int Port = 45345;
        private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.1.5.10");


        public static ServiceDiscovery Instance { get; } = new ServiceDiscovery();

        private ServiceDiscovery()
        {
        }

        public void Start()
        {
            var ips = NetworkTools.GetAddressList();

            ips.ForEach(address =>
            {
                _logger.Debug($"Starting discovery listener at {MulticastAddress}:{Port}");
                var udpClient = new UdpClient(AddressFamily.InterNetwork) {EnableBroadcast = true};
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(new IPEndPoint(address, Port));
                udpClient.JoinMulticastGroup(MulticastAddress, address);
                udpClient.BeginReceive(OnDataReceived, udpClient);
            });
        }

        private void OnDataReceived(IAsyncResult ar)
        {
            var udpClient = (UdpClient) ar.AsyncState;
            var mEndPoint = new IPEndPoint(IPAddress.Any, Port);
            var request = udpClient.EndReceive(ar, ref mEndPoint);
            var mRequest = Encoding.UTF8.GetString(request);
            var incoming = JsonObject.Parse(mRequest);

            _logger.Debug($"Discovery incoming message {mRequest}");

            var discovery = incoming.Get("context")?.Contains("discovery") ?? false;
            if (discovery)
            {
                var addresses = NetworkTools.GetPrivateAddressList();
                var ipString = incoming.Get("address");
                if (string.IsNullOrEmpty(ipString))
                {
                    var notify = ErrorMessage("missing address");
                    SendResponse(notify, mEndPoint, udpClient);
                }
                else
                {
                    var interfaceAddress = InterfaceAddress(ipString, addresses);
                    var notify = DiscoveryResponse(interfaceAddress);
                    _logger.Debug($"Replying to {ipString} discovery message with {notify}");
                    SendResponse(notify, mEndPoint, udpClient);
                }
            }
            else
            {
                var notify = ErrorMessage("unsupported action");
                SendResponse(notify, mEndPoint, udpClient);
            }
            udpClient.BeginReceive(OnDataReceived, udpClient);
        }

        private void SendResponse(Dictionary<string, object> notify, IPEndPoint mEndPoint, UdpClient udpClient)
        {
            var response = Encoding.UTF8.GetBytes(JsonSerializer.SerializeToString(notify));
            udpClient.Send(response, response.Length, mEndPoint);
        }

        private static Dictionary<string, object> ErrorMessage(string errorMessage)
        {
            var notify = new Dictionary<string, object>
            {
                {"context", "error"},
                {"description", errorMessage},
            };
            return notify;
        }

        private static Dictionary<string, object> DiscoveryResponse(string interfaceAddress)
        {
            var notify = new Dictionary<string, object>
            {
                {"context", "notify"},
                {"address", interfaceAddress},
                {"name", Environment.GetEnvironmentVariable("COMPUTERNAME")},
                {"port", UserSettings.Instance.ListeningPort}
            };
            return notify;
        }

        private static string InterfaceAddress(string ipString, List<string> addresses)
        {
            var clientAddress = IPAddress.Parse(ipString);
            var interfaceAddress = string.Empty;
            foreach (var address in addresses)
            {
                var ifAddress = IPAddress.Parse(address);
                var subnetMask = NetworkTools.GetSubnetMask(address);

                var firstNetwork = NetworkTools.GetNetworkAddress(ifAddress, subnetMask);
                var secondNetwork = NetworkTools.GetNetworkAddress(clientAddress, subnetMask);
                if (!firstNetwork.Equals(secondNetwork)) continue;
                interfaceAddress = ifAddress.ToString();
                break;
            }
            return interfaceAddress;
        }
    }
}