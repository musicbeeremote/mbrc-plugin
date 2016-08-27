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
        private UdpClient _mListener;

        public static ServiceDiscovery Instance { get; } = new ServiceDiscovery();

        private ServiceDiscovery()
        {
        }

        public void Start()
        {
            _logger.Debug($"Starting discovery listener at {MulticastAddress}:{Port}");
            _mListener = new UdpClient(Port, AddressFamily.InterNetwork) {EnableBroadcast = true};
            _mListener.JoinMulticastGroup(MulticastAddress);
            _mListener.BeginReceive(OnDataReceived, null);
        }

        private void OnDataReceived(IAsyncResult ar)
        {
            var mEndPoint = new IPEndPoint(IPAddress.Any, Port);
            var request = _mListener.EndReceive(ar, ref mEndPoint);
            var mRequest = Encoding.UTF8.GetString(request);
            var incoming = JsonObject.Parse(mRequest);

            _logger.Debug($"Discovery incoming message {mRequest}");

            var discovery = incoming.Get("context")?.Contains("discovery") ?? false;
            if (discovery)
            {
                var addresses = NetworkTools.GetPrivateAddressList();
                var clientAddress = IPAddress.Parse(incoming.Get("address"));
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

                var notify = new Dictionary<string, object>
                {
                    {"context", "notify"},
                    {"address", interfaceAddress},
                    {"name", Environment.GetEnvironmentVariable("COMPUTERNAME")},
                    {"port", UserSettings.Instance.ListeningPort}
                };

                _logger.Debug($"Replying to discovery message with {notify}");

                var response = Encoding.UTF8.GetBytes(JsonSerializer.SerializeToString(notify));
                _mListener.Send(response, response.Length, mEndPoint);
            }
            else
            {
                var notify = new Dictionary<string, object>
                {
                    {"context", "error"},
                    {"description", "unsupported action"},
                };
                var response = Encoding.UTF8.GetBytes(JsonSerializer.SerializeToString(notify));
                _mListener.Send(response, response.Length, mEndPoint);
            }
            _mListener.BeginReceive(OnDataReceived, null);
        }

    }
}
