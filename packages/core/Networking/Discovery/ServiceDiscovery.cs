using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin.Networking.Discovery
{
    public class ServiceDiscovery : IServiceDiscovery
    {
        private const int Port = 45345;
        private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.1.5.10");
        private readonly object _lockObject = new object();

        private readonly IPluginLogger _logger;
        private readonly INetworkTools _networkTools;
        private readonly List<UdpClient> _udpClients = new List<UdpClient>();
        private readonly IUserSettings _userSettings;

        private volatile bool _isRunning;

        public ServiceDiscovery(
            INetworkTools networkTools,
            IUserSettings userSettings,
            IPluginLogger logger)
        {
            _networkTools = networkTools;
            _userSettings = userSettings;
            _logger = logger;
            _isRunning = false;
        }

        public void StartListening()
        {
            lock (_lockObject)
            {
                if (_isRunning)
                {
                    _logger.Debug("Service discovery is already running");
                    return;
                }

                _logger.Debug("Starting service discovery");
                var ips = _networkTools.GetAddressList();
                var successfulClients = 0;

                foreach (var address in ips)
                    if (TryStartListener(address))
                        successfulClients++;

                if (successfulClients > 0)
                {
                    _isRunning = true;
                    _logger.Debug($"Service discovery started successfully on {successfulClients} interface(s)");
                }
                else
                {
                    _logger.Warn("Failed to start service discovery on any network interface");
                }
            }
        }

        public void StopListening()
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                {
                    _logger.Debug("Service discovery is already stopped");
                    return;
                }

                _logger.Debug("Stopping service discovery");
                _isRunning = false;

                // Create a copy to avoid modification during iteration
                var clientsToClose = new List<UdpClient>(_udpClients);
                _udpClients.Clear();

                foreach (var client in clientsToClose)
                    DisposeUdpClient(client);

                _logger.Debug("Service discovery stopped");
            }
        }

        public void Dispose()
        {
            StopListening();
            GC.SuppressFinalize(this);
        }

        private void OnDataReceived(IAsyncResult ar)
        {
            var udpClient = (UdpClient)ar.AsyncState;

            try
            {
                if (!_isRunning || udpClient == null)
                    return;

                var endPoint = new IPEndPoint(IPAddress.Any, Port);
                var request = udpClient.EndReceive(ar, ref endPoint);

                if (request.Length == 0)
                {
                    _logger.Debug("Received empty discovery request");
                    return;
                }

                var requestString = Encoding.UTF8.GetString(request);
                _logger.Debug($"Discovery incoming message from {endPoint}: {requestString}");

                if (string.IsNullOrWhiteSpace(requestString))
                    return;

                ProcessDiscoveryMessage(requestString, endPoint, udpClient);
            }
            catch (ObjectDisposedException)
            {
                _logger.Debug("UDP client disposed while receiving data");
            }
            catch (SocketException se) when (se.ErrorCode == 10054) // Connection reset by peer
            {
                _logger.Debug($"Discovery client connection reset: {se.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in discovery data received");
            }
            finally
            {
                // Restart receiving if still running and client is valid
                if (_isRunning && udpClient != null)
                    try
                    {
                        udpClient.BeginReceive(OnDataReceived, udpClient);
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.Debug("UDP client was disposed, stopping receive loop");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error restarting discovery receive");
                    }
            }
        }

        private void HandleDiscoveryRequest(JObject incoming, IPEndPoint endPoint, UdpClient udpClient)
        {
            var ipString = incoming["address"]?.ToString();

            if (string.IsNullOrEmpty(ipString))
            {
                var errorResponse = ErrorMessage("missing address");
                SendResponse(errorResponse, endPoint, udpClient);
                return;
            }

            try
            {
                var addresses = _networkTools.GetPrivateAddressList();
                var interfaceAddress = InterfaceAddress(ipString, addresses);

                if (string.IsNullOrEmpty(interfaceAddress))
                {
                    _logger.Debug($"No suitable interface found for client {ipString}");
                    var errorResponse = ErrorMessage("no suitable interface found");
                    SendResponse(errorResponse, endPoint, udpClient);
                    return;
                }

                var response = DiscoveryResponse(interfaceAddress);
                _logger.Debug($"Replying to {ipString} discovery request with interface {interfaceAddress}");
                SendResponse(response, endPoint, udpClient);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling discovery request from {ipString}");
                var errorResponse = ErrorMessage("internal error");
                SendResponse(errorResponse, endPoint, udpClient);
            }
        }

        private void SendResponse(Dictionary<string, object> response, IPEndPoint endPoint, UdpClient udpClient)
        {
            if (response == null || endPoint == null || udpClient == null)
                return;

            try
            {
                var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response));

                if (data.Length > 0)
                {
                    udpClient.Send(data, data.Length, endPoint);
                    _logger.Debug($"Sent discovery response to {endPoint}");
                }
            }
            catch (ObjectDisposedException)
            {
                _logger.Debug($"Cannot send response to {endPoint} - UDP client disposed");
            }
            catch (SocketException se)
            {
                _logger.Debug($"Socket error sending discovery response to {endPoint}: {se.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending discovery response to {endPoint}");
            }
        }

        private static Dictionary<string, object> ErrorMessage(string errorMessage)
        {
            return new Dictionary<string, object>
            {
                { "context", "error" },
                { "description", errorMessage }
            };
        }

        private Dictionary<string, object> DiscoveryResponse(string interfaceAddress)
        {
            return new Dictionary<string, object>
            {
                { "context", "notify" },
                { "address", interfaceAddress },
                { "name", Environment.MachineName },
                { "port", _userSettings.ListeningPort }
            };
        }

        /// <summary>
        ///     Attempts to start a UDP listener on the specified address
        /// </summary>
        private bool TryStartListener(IPAddress address)
        {
            try
            {
                _logger.Debug($"Starting discovery listener at {MulticastAddress}:{Port} for interface {address}");

                var udpClient = new UdpClient(AddressFamily.InterNetwork)
                {
                    EnableBroadcast = true
                };

                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(new IPEndPoint(address, Port));
                udpClient.JoinMulticastGroup(MulticastAddress, address);

                lock (_lockObject)
                {
                    _udpClients.Add(udpClient);
                }

                udpClient.BeginReceive(OnDataReceived, udpClient);
                return true;
            }
            catch (SocketException se)
            {
                _logger.Debug($"Socket error starting discovery listener on {address}: {se.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to start discovery listener on {address}");
                return false;
            }
        }

        /// <summary>
        ///     Safely disposes a UDP client with proper error handling
        /// </summary>
        private void DisposeUdpClient(UdpClient client)
        {
            if (client == null)
                return;

            try
            {
                try
                {
                    client.DropMulticastGroup(MulticastAddress);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Error dropping multicast group: {ex.Message}");
                }

                client.Close();
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error closing UDP client: {ex.Message}");
            }
            finally
            {
                try
                {
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Error disposing UDP client: {ex.Message}");
                }
            }
        }

        /// <summary>
        ///     Processes the discovery message and sends appropriate response
        /// </summary>
        private void ProcessDiscoveryMessage(string message, IPEndPoint endPoint, UdpClient udpClient)
        {
            try
            {
                var incoming = JObject.Parse(message);
                var context = incoming["context"]?.ToString();

                if (context?.IndexOf("discovery", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    HandleDiscoveryRequest(incoming, endPoint, udpClient);
                }
                else
                {
                    var response = ErrorMessage("unsupported action");
                    SendResponse(response, endPoint, udpClient);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error parsing discovery message from {endPoint}: {ex.Message}");
                var errorResponse = ErrorMessage("invalid message format");
                SendResponse(errorResponse, endPoint, udpClient);
            }
        }

        private string InterfaceAddress(string ipString, List<string> addresses)
        {
            try
            {
                if (string.IsNullOrEmpty(ipString))
                    return string.Empty;

                var clientAddress = IPAddress.Parse(ipString);

                foreach (var address in addresses)
                    try
                    {
                        var ifAddress = IPAddress.Parse(address);
                        var subnetMask = _networkTools.GetSubnetMask(address);
                        var firstNetwork = _networkTools.GetNetworkAddress(ifAddress, subnetMask);
                        var secondNetwork = _networkTools.GetNetworkAddress(clientAddress, subnetMask);

                        if (firstNetwork.Equals(secondNetwork))
                            return ifAddress.ToString();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Error checking interface {address}: {ex.Message}");
                    }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error finding interface address for {ipString}: {ex.Message}");
            }

            return string.Empty;
        }
    }
}
