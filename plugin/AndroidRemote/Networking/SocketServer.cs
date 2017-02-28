using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.AndroidRemote.Utilities;
using NLog;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    /// <summary>
    /// The socket server.
    /// </summary>
    public sealed class SocketServer : IDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly ProtocolHandler _handler;

        private readonly ConcurrentDictionary<string, Socket> _availableWorkerSockets;

        /// <summary>
        /// The main socket. This is the Socket that listens for new client connections.
        /// </summary>
        private Socket _mainSocket;

        /// <summary>
        /// The worker callback.
        /// </summary>
        private AsyncCallback _workerCallback;

        private bool _isRunning;
        private Timer _pingTimer;
        private const string NewLine = "\r\n";

        /// <summary>
        /// Returns the Instance of the signleton socketserver
        /// </summary>
        public static SocketServer Instance { get; } = new SocketServer();

        /// <summary>
        ///
        /// </summary>
        private SocketServer()
        {
            _handler = new ProtocolHandler();
            IsRunning = false;
            _availableWorkerSockets = new ConcurrentDictionary<string, Socket>();
        }

        /// <summary>
        ///
        /// </summary>
        public bool IsRunning
        {
            get { return _isRunning; }
            private set
            {
                _isRunning = value;
                EventBus.FireEvent(new MessageEvent(EventType.SocketStatusChange, _isRunning));
            }
        }

        public IPAddress GetIpAddress(string client)
        {
            Socket socket;
            var clientExists = _availableWorkerSockets.TryGetValue(client, out socket);

            if (!clientExists || socket == null) return null;

            try
            {
                return ((IPEndPoint)socket.RemoteEndPoint).Address;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex);
            }
            return null;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="clientId"> </param>
        public void KickClient(string clientId)
        {
            try
            {
                Socket workerSocket;
                if (!_availableWorkerSockets.TryRemove(clientId, out workerSocket)) return;
                workerSocket.Close();
                workerSocket.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "While kicking a client");
            }
        }

        /// <summary>
        /// It stops the SocketServer.
        /// </summary>
        /// <returns></returns>
        public void Stop()
        {
            _logger.Debug("Stopping socket service");
            try
            {
                _mainSocket?.Close();

                foreach (var wSocket in _availableWorkerSockets.Values)
                {
                    if (wSocket == null) continue;
                    wSocket.Close();
                    wSocket.Dispose();
                }
                _mainSocket = null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "While stopping the socket service");
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// It starts the SocketServer.
        /// </summary>
        /// <returns></returns>
        public void Start()
        {
            _logger.Debug($"Socket starts listening on port: {UserSettings.Instance.ListeningPort}");
            try
            {
                _mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                // Create the listening socket.
                var ipLocal = new IPEndPoint(IPAddress.Any, Convert.ToInt32(UserSettings.Instance.ListeningPort));
                // Bind to local IP address.
                _mainSocket.Bind(ipLocal);
                // Start Listening.
                _mainSocket.Listen(4);
                // Create the call back for any client connections.
                _mainSocket.BeginAccept(OnClientConnect, null);
                IsRunning = true;

                _pingTimer = new Timer(15000);
                _pingTimer.Elapsed += PingTimerOnElapsed;
                _pingTimer.Enabled = true;
            }
            catch (SocketException se)
            {
                _logger.Error(se, "While starting the socket service");
            }
        }

        private void PingTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            Send(new SocketMessage("ping", string.Empty).ToJsonString());
            _logger.Debug($"Ping: {DateTime.UtcNow}");
        }

        /// <summary>
        /// Restarts the main socket that is listening for new clients.
        /// Useful when the user wants to change the listening port.
        /// </summary>
        public void RestartSocket()
        {
            Stop();
            Start();
        }

        // this is the call back function,
        private void OnClientConnect(IAsyncResult ar)
        {
            try
            {
                // Here we complete/end the BeginAccept asynchronous call
                // by calling EndAccept() - Which returns the reference
                // to a new Socket object.
                var workerSocket = _mainSocket.EndAccept(ar);

                // Validate If client should connect.
                var ipAddress = ((IPEndPoint)workerSocket.RemoteEndPoint).Address;
                var ipString = ipAddress.ToString();

                var isAllowed = false;
                switch (UserSettings.Instance.FilterSelection)
                {
                    case FilteringSelection.Specific:
                        foreach (var source in UserSettings.Instance.IpAddressList)
                        {
                            if (string.Compare(ipString, source, StringComparison.Ordinal) == 0)
                            {
                                isAllowed = true;
                            }
                        }
                        break;

                    case FilteringSelection.Range:
                        var connectingAddress = ipString.Split(".".ToCharArray(),
                            StringSplitOptions.RemoveEmptyEntries);
                        var baseIp = UserSettings.Instance.BaseIp.Split(".".ToCharArray(),
                            StringSplitOptions.RemoveEmptyEntries);
                        if (connectingAddress[0] == baseIp[0] && connectingAddress[1] == baseIp[1] &&
                            connectingAddress[2] == baseIp[2])
                        {
                            int connectingAddressLowOctet;
                            int baseIpAddressLowOctet;
                            int.TryParse(connectingAddress[3], out connectingAddressLowOctet);
                            int.TryParse(baseIp[3], out baseIpAddressLowOctet);
                            if (connectingAddressLowOctet >= baseIpAddressLowOctet && baseIpAddressLowOctet <= UserSettings.Instance.LastOctetMax)
                            {
                                isAllowed = true;
                            }
                        }
                        break;

                    default:
                        isAllowed = true;
                        break;
                }

                if (Equals(ipAddress, IPAddress.Loopback))
                {
                    isAllowed = true;
                }

                if (!isAllowed)
                {
                    workerSocket.Send(Encoding.UTF8.GetBytes(new SocketMessage(Constants.NotAllowed, string.Empty).ToJsonString()));
                    workerSocket.Close();
                    _logger.Debug($"Client {ipString} was force disconnected IP was not in the allowed addresses");
                    _mainSocket.BeginAccept(OnClientConnect, null);
                    return;
                }

                var clientId = IdGenerator.GetUniqueKey();

                if (!_availableWorkerSockets.TryAdd(clientId, workerSocket)) return;
                // Inform the the Protocol Handler that a new Client has been connected, prepare for handshake.
                EventBus.FireEvent(new MessageEvent(EventType.ActionClientConnected, string.Empty, clientId));

                // Let the worker Socket do the further processing
                // for the just connected client.
                WaitForData(workerSocket, clientId);
            }
            catch (ObjectDisposedException)
            {
                _logger.Debug($"{DateTime.Now.ToString(CultureInfo.InvariantCulture)} : OnClientConnection: Socket has been closed\n");
            }
            catch (SocketException se)
            {
                _logger.Debug(se, "On client connect");
            }
            catch (Exception ex)
            {
                _logger.Debug($"{DateTime.Now.ToString(CultureInfo.InvariantCulture)} : OnClientConnect Exception : {ex.Message}\n");
            }
            finally
            {
                try
                {
                    // Since the main Socket is now free, it can go back and
                    // wait for the other clients who are attempting to connect
                    _mainSocket.BeginAccept(OnClientConnect, null);
                }
                catch (Exception e)
                {
                    _logger.Debug(DateTime.Now.ToString(CultureInfo.InvariantCulture) +
                                  $" : OnClientConnect Exception : " + e.Message + "\n");
                }
            }
        }

        // Start waiting for data from the client
        private void WaitForData(Socket socket, string clientId, SocketPacket packet = null)
        {
            try
            {
                if (_workerCallback == null)
                {
                    // Specify the call back function which is to be
                    // invoked when there is any write activity by the
                    // connected client.
                    _workerCallback = OnDataReceived;
                }

                var socketPacket = packet ?? new SocketPacket(socket, clientId);

                socket.BeginReceive(socketPacket.DataBuffer, 0, socketPacket.DataBuffer.Length, SocketFlags.None,
                    _workerCallback, socketPacket);
            }
            catch (SocketException se)
            {
                _logger.Debug(se, "On WaitForData");
                if (se.ErrorCode == 10053)
                {
                    EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, clientId));
                }
            }
        }

        // This is the call back function which will be invoked when the socket
        // detects any client writing of data on the stream
        private void OnDataReceived(IAsyncResult ar)
        {
            var clientId = string.Empty;
            try
            {
                var socketData = (SocketPacket)ar.AsyncState;
                // Complete the BeginReceive() asynchronus call by EndReceive() method
                // which will return the number of characters written to the stream
                // by the client.

                clientId = socketData.ClientId;

                var iRx = socketData.MCurrentSocket.EndReceive(ar);
                var chars = new char[iRx + 1];

                var decoder = Encoding.UTF8.GetDecoder();

                decoder.GetChars(socketData.DataBuffer, 0, iRx, chars, 0);
                if (chars.Length == 1 && chars[0] == 0)
                {
                    socketData.MCurrentSocket.Close();
                    socketData.MCurrentSocket.Dispose();
                    return;
                }
                var message = new string(chars).Replace("\0", "");

                if (string.IsNullOrEmpty(message))
                    return;

                if (!message.EndsWith("\r\n"))
                {
                    socketData.Partial.Append(message);
                    WaitForData(socketData.MCurrentSocket, socketData.ClientId, socketData);
                    return;
                }

                if (socketData.Partial.Length > 0)
                {
                    message = socketData.Partial.Append(message).ToString();
                    socketData.Partial.Clear();
                }

                _handler.ProcessIncomingMessage(message, socketData.ClientId);

                // Continue the waiting for data on the Socket.
                WaitForData(socketData.MCurrentSocket, socketData.ClientId, socketData);
            }
            catch (ObjectDisposedException)
            {
                EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, clientId));
                _logger.Debug(DateTime.Now.ToString(CultureInfo.InvariantCulture) +
                              " : OnDataReceived: Socket has been closed\n");
            }
            catch (SocketException se)
            {
                if (se.ErrorCode == 10054) // Error code for Connection reset by peer
                {
                    Socket deadSocket;
                    if (_availableWorkerSockets.ContainsKey(clientId))
                        _availableWorkerSockets.TryRemove(clientId, out deadSocket);
                    EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, clientId));
                }
                else
                {
                    _logger.Error(se, "On DataReceive");
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="message"></param>
        /// <param name="clientId"></param>
        public void Send(string message, string clientId)
        {
            _logger.Debug($"sending-{clientId}:{message}");

            if (clientId.Equals("all"))
            {
                Send(message);
                return;
            }
            try
            {
                var data = Encoding.UTF8.GetBytes(message + NewLine);
                Socket wSocket;
                if (_availableWorkerSockets.TryGetValue(clientId, out wSocket))
                {
                    wSocket.Send(data);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "While sending message to specific client");
            }
        }

        private void RemoveDeadSocket(string clientId)
        {
            Socket worker;
            _availableWorkerSockets.TryRemove(clientId, out worker);
            worker?.Dispose();
        }

        public void Broadcast(BroadcastEvent broadcastEvent)
        {
            _logger.Debug($"broadcasting message {broadcastEvent}");

            try
            {
                foreach (var key in _availableWorkerSockets.Keys)
                {
                    Socket worker;
                    if (!_availableWorkerSockets.TryGetValue(key, out worker)) continue;
                    var isConnected = worker != null && worker.Connected;
                    if (!isConnected)
                    {
                        RemoveDeadSocket(key);
                        EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, key));
                    }

                    if (!isConnected || !Authenticator.IsClientAuthenticated(key) ||
                        !Authenticator.IsClientBroadcastEnabled(key)) continue;

                    var clientProtocol = Authenticator.ClientProtocolVersion(key);
                    var message = broadcastEvent.GetMessage(clientProtocol);
                    var data = Encoding.UTF8.GetBytes(message + NewLine);
                    worker.Send(data);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "While sending message to all available clients");
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="message"></param>
        public void Send(string message)
        {
            _logger.Debug($"sending-all: {message}");

            try
            {
                var data = Encoding.UTF8.GetBytes(message + NewLine);

                foreach (var key in _availableWorkerSockets.Keys)
                {
                    Socket worker;
                    if (!_availableWorkerSockets.TryGetValue(key, out worker)) continue;
                    var isConnected = (worker != null && worker.Connected);
                    if (!isConnected)
                    {
                        RemoveDeadSocket(key);
                        EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, key));
                    }
                    if (isConnected && Authenticator.IsClientAuthenticated(key) &&
                        Authenticator.IsClientBroadcastEnabled(key))
                    {
                        worker.Send(data);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "While sending message to all available clients");
            }
        }

        public List<Socket> GetConnectedSockets()
        {
            return (List<Socket>)_availableWorkerSockets.Values;
        }

        /// <summary>
        /// Disposes anything Related to the socket server at the end of life of the Object.
        /// </summary>
        public void Dispose()
        {
            _mainSocket.Dispose();
        }
    }
}