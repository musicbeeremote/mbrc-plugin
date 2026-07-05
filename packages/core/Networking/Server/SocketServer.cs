using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using MusicBeePlugin.Constants;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Common;
using MusicBeePlugin.Utilities.Network;
using Timer = System.Timers.Timer;

namespace MusicBeePlugin.Networking.Server
{
    /// <summary>
    ///     The socket server.
    /// </summary>
    public class SocketServer : ISocketServer
    {
        private static readonly byte[] NewLineBytes = Encoding.UTF8.GetBytes(ProtocolConstants.MessageTerminator);
        private static readonly char[] DotSeparator = { '.' };

        private readonly IAuthenticator _authenticator;

        private readonly ConcurrentDictionary<string, Socket> _availableWorkerSockets;
        private readonly IEventAggregator _eventBus;
        private readonly IProtocolHandler _handler;
        private readonly object _lockObject = new object();
        private readonly IPluginLogger _logger;
        private readonly IUserSettings _userSettings;

        private volatile bool _isRunning;

        /// <summary>
        ///     The main socket. This is the Socket that listens for new client connections.
        /// </summary>
        private Socket _mainSocket;

        private Timer _pingTimer;

        /// <summary>
        ///     The worker callback.
        /// </summary>
        private AsyncCallback _workerCallback;

        public SocketServer(
            IProtocolHandler handler,
            IAuthenticator authenticator,
            IUserSettings userSettings,
            IEventAggregator eventBus,
            IPluginLogger logger)
        {
            _handler = handler;
            _authenticator = authenticator;
            _userSettings = userSettings;
            _eventBus = eventBus;
            _logger = logger;
            IsRunning = false;
            _availableWorkerSockets = new ConcurrentDictionary<string, Socket>();

            // Subscribe to protocol handler events
            _handler.ForceClientDisconnect += KickClient;
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                _isRunning = value;
                _eventBus.PublishAsync(SocketStatusChangeEvent.Create(_isRunning));
            }
        }

        /// <summary>
        ///     Gets a short identifier for logging purposes.
        ///     Returns client's ShortId if available, otherwise first 6 chars of connectionId.
        /// </summary>
        private string GetShortId(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return "?";

            var client = _authenticator.Client(connectionId);
            if (client != null)
                return client.ShortId;

            // Fallback to truncated connectionId
            return connectionId.Length > 6 ? connectionId.Substring(0, 6) : connectionId;
        }

        /// <summary>
        ///     Disposes anything Related to the socket server at the end of life of the Object.
        /// </summary>
        public void Dispose()
        {
            // Unsubscribe from events
            if (_handler != null)
                _handler.ForceClientDisconnect -= KickClient;

            StopListening();

            _pingTimer?.Dispose();
            _pingTimer = null;

            _mainSocket?.Dispose();
            _mainSocket = null;

            GC.SuppressFinalize(this);
        }


        /// <summary>
        ///     Forcefully disconnects a client.
        /// </summary>
        /// <param name="connectionId">The ID of the client to kick</param>
        public void KickClient(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;

            try
            {
                if (!_availableWorkerSockets.TryRemove(connectionId, out var workerSocket))
                    return;

                try
                {
                    workerSocket?.Shutdown(SocketShutdown.Both);
                }
                catch (Exception shutdownEx)
                {
                    _logger.Debug($"Error shutting down socket for client {GetShortId(connectionId)}: {shutdownEx.Message}");
                }
                finally
                {
                    workerSocket?.Close();
                    workerSocket?.Dispose();
                }

                OnClientDisconnected(connectionId);
                _logger.Debug($"Client {GetShortId(connectionId)} has been kicked");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"While kicking client {connectionId}");
            }
        }

        /// <summary>
        ///     It stops the SocketServer.
        /// </summary>
        /// <returns></returns>
        public void StopListening()
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                    return;

                _logger.Debug("Stopping socket service");
                try
                {
                    _pingTimer?.Stop();

                    // Close main socket first to stop accepting new connections
                    if (_mainSocket != null)
                        try
                        {
                            _mainSocket.Close();
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Error closing main socket: {ex.Message}");
                        }

                    // Close all worker sockets
                    foreach (var kvp in _availableWorkerSockets)
                        try
                        {
                            kvp.Value?.Close();
                            kvp.Value?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Error closing worker socket {kvp.Key}: {ex.Message}");
                        }

                    _availableWorkerSockets.Clear();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "While stopping the socket service");
                }
                finally
                {
                    IsRunning = false;
                }
            }
        }

        /// <summary>
        ///     It starts the SocketServer.
        /// </summary>
        /// <returns></returns>
        public void StartListening()
        {
            lock (_lockObject)
            {
                if (_isRunning)
                {
                    _logger.Debug("Socket server is already running");
                    return;
                }

                _logger.Debug($"Socket starts listening on port: {_userSettings.ListeningPort}");
                try
                {
                    // Dispose existing socket if any
                    _mainSocket?.Dispose();

                    _mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                    // Enable socket reuse to avoid "Address already in use" errors
                    _mainSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    var ipLocal = new IPEndPoint(IPAddress.Any, (int)_userSettings.ListeningPort);
                    _mainSocket.Bind(ipLocal);
                    _mainSocket.Listen(NetworkConstants.SocketBacklogSize);
                    _mainSocket.BeginAccept(OnClientConnect, null);

                    IsRunning = true;

                    // Setup ping timer
                    _pingTimer?.Dispose();
                    _pingTimer = new Timer(NetworkConstants.PingIntervalMs);
                    _pingTimer.Elapsed += PingTimerOnElapsed;
                    _pingTimer.Enabled = true;

                    _logger.Debug($"Socket server started successfully on port {_userSettings.ListeningPort}");
                }
                catch (SocketException se)
                {
                    _logger.LogError(se,
                        $"Failed to start socket service on port {_userSettings.ListeningPort}. Error code: {se.ErrorCode}");
                    IsRunning = false;

                    // Clean up on failure
                    try
                    {
                        _mainSocket?.Close();
                        _mainSocket?.Dispose();
                        _mainSocket = null;
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.Debug($"Error during cleanup after start failure: {cleanupEx.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while starting socket service");
                    IsRunning = false;
                }
            }
        }

        /// <summary>
        ///     Restarts the main socket that is listening for new clients.
        ///     Useful when the user wants to change the listening port.
        /// </summary>
        public void RestartSocket()
        {
            _logger.Debug("Restarting socket server");
            StopListening();

            // Give a small delay to ensure cleanup is complete
            Thread.Sleep(NetworkConstants.SocketRestartDelayMs);

            StartListening();
        }

        /// <summary>
        ///     Sends a message to a specific client or all clients.
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="connectionId">The client ID, or "all" to send to all clients</param>
        public void Send(string message, string connectionId)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(connectionId))
                return;

            // Handle broadcast case - delegate to Send(message) which has its own logging
            if (connectionId.Equals(NetworkConstants.BroadcastAllClientId, StringComparison.OrdinalIgnoreCase))
            {
                Send(message);
                return;
            }

            _logger.Debug($"sending-{GetShortId(connectionId)}:{message}");

            if (!_availableWorkerSockets.TryGetValue(connectionId, out var wSocket))
            {
                _logger.Debug($"Connection {GetShortId(connectionId)} not found");
                return;
            }

            try
            {
                // Send without checking Connected - matches original behavior
                // Socket.Connected can return false in edge cases even when connection is usable
                var messageBytes = Encoding.UTF8.GetBytes(message);
                var totalBytes = new byte[messageBytes.Length + NewLineBytes.Length];
                Array.Copy(messageBytes, 0, totalBytes, 0, messageBytes.Length);
                Array.Copy(NewLineBytes, 0, totalBytes, messageBytes.Length, NewLineBytes.Length);
                wSocket.Send(totalBytes);
            }
            catch (SocketException se) when (se.ErrorCode == NetworkConstants.SocketErrorConnectionReset)
            {
                _logger.Debug($"Connection {GetShortId(connectionId)} was reset during send");
                RemoveDeadSocket(connectionId);
                OnClientDisconnected(connectionId);
            }
            catch (ObjectDisposedException)
            {
                _logger.Debug($"Connection {GetShortId(connectionId)} socket already disposed");
                _availableWorkerSockets.TryRemove(connectionId, out _);
                OnClientDisconnected(connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"While sending message to client {GetShortId(connectionId)}");
            }
        }


        public void Broadcast(BroadcastEvent broadcastEvent)
        {
            if (broadcastEvent == null)
                return;

            SendToAuthenticatedClients(
                connectionId =>
                {
                    var clientProtocol = _authenticator.Client(connectionId)?.ClientProtocolVersion ?? 2;
                    return broadcastEvent.GetMessage(clientProtocol);
                },
                "broadcasting event");
        }

        /// <summary>
        ///     Sends a message to all authenticated clients.
        /// </summary>
        /// <param name="message">The message to send</param>
        public void Send(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            _logger.Debug($"sending-all: {message}");

            SendToAuthenticatedClients(_ => message, "sending to all clients");
        }

        private void OnClientDisconnected(string connectionId)
        {
            _authenticator.RemoveClientOnDisconnect(connectionId);
        }

        /// <summary>
        ///     Iterates over authenticated broadcast-enabled clients and sends messages.
        ///     Handles connection errors and cleanup of dead connections.
        /// </summary>
        /// <param name="getMessageForClient">Function that returns the message to send for a given client ID, or null to skip</param>
        /// <param name="operationName">Name of the operation for logging purposes</param>
        private void SendToAuthenticatedClients(Func<string, string> getMessageForClient, string operationName)
        {
            var clientsToRemove = new List<string>();

            try
            {
                foreach (var kvp in _availableWorkerSockets)
                {
                    var connectionId = kvp.Key;
                    var worker = kvp.Value;

                    if (worker?.Connected != true)
                    {
                        clientsToRemove.Add(connectionId);
                        continue;
                    }

                    if (!_authenticator.IsClientAuthenticated(connectionId) ||
                        !_authenticator.IsClientBroadcastEnabled(connectionId))
                        continue;

                    var message = getMessageForClient(connectionId);
                    if (string.IsNullOrEmpty(message))
                        continue;

                    if (!TrySendToSocket(worker, message, connectionId, operationName))
                    {
                        clientsToRemove.Add(connectionId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"While {operationName}");
            }
            finally
            {
                CleanupDeadConnections(clientsToRemove);
            }
        }

        /// <summary>
        ///     Attempts to send a message to a socket, handling connection reset errors.
        /// </summary>
        /// <returns>True if send succeeded or error was non-fatal, false if client should be removed</returns>
        private bool TrySendToSocket(Socket socket, string message, string connectionId, string operationName)
        {
            try
            {
                SendMessageToSocket(socket, message);
                return true;
            }
            catch (SocketException se) when (se.ErrorCode == NetworkConstants.SocketErrorConnectionReset)
            {
                _logger.Debug($"Client {GetShortId(connectionId)} connection reset during {operationName}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error {operationName} to client {GetShortId(connectionId)}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///     Removes dead connections and notifies the authenticator.
        /// </summary>
        private void CleanupDeadConnections(List<string> connectionIds)
        {
            foreach (var connectionId in connectionIds)
            {
                RemoveDeadSocket(connectionId);
                OnClientDisconnected(connectionId);
            }
        }

        private void PingTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            Send(new SocketMessage("ping", string.Empty).ToJsonString());
            _logger.Debug($"Ping: {DateTime.UtcNow}");
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

                if (!IsClientAllowed(ipAddress, ipString))
                {
                    RejectConnection(workerSocket, ipString);
                    return;
                }

                var connectionId = IdGenerator.GetUniqueKey();

                if (!_availableWorkerSockets.TryAdd(connectionId, workerSocket))
                    return;

                _authenticator.AddClientOnConnect(connectionId);
                WaitForData(workerSocket, connectionId);
            }
            catch (ObjectDisposedException)
            {
                _logger.Debug("OnClientConnection: Socket has been closed");
            }
            catch (SocketException se)
            {
                _logger.LogError(se, "On client connect");
            }
            catch (Exception ex)
            {
                _logger.Debug($"OnClientConnect Exception: {ex.Message}");
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
                    _logger.Debug($"OnClientConnect Exception: {e.Message}");
                }
            }
        }

        // Start waiting for data from the client
        private void WaitForData(Socket socket, string connectionId, SocketPacket packet = null)
        {
            try
            {
                if (_workerCallback == null)
                    // Specify the call back function which is to be
                    // invoked when there is any write activity by the
                    // connected client.
                    _workerCallback = OnDataReceived;

                var socketPacket = packet ?? new SocketPacket(socket, connectionId);

                socket.BeginReceive(socketPacket.DataBuffer, 0, socketPacket.DataBuffer.Length, SocketFlags.None,
                    _workerCallback, socketPacket);
            }
            catch (SocketException se)
            {
                _logger.LogError(se, "On WaitForData");
                if (se.ErrorCode == 10053)
                    OnClientDisconnected(connectionId);
            }
        }

        // This is the call back function which will be invoked when the socket
        // detects any client writing of data on the stream
        private void OnDataReceived(IAsyncResult ar)
        {
            var connectionId = string.Empty;
            try
            {
                var socketData = (SocketPacket)ar.AsyncState;
                // Complete the BeginReceive() asynchronous call by EndReceive() method
                // which will return the number of characters written to the stream
                // by the client.

                connectionId = socketData.ConnectionId;

                var iRx = socketData.MCurrentSocket.EndReceive(ar);
                var chars = new char[iRx + 1];

                var decoder = Encoding.UTF8.GetDecoder();

                decoder.GetChars(socketData.DataBuffer, 0, iRx, chars, 0);
                if (chars.Length == 1 && chars[0] == 0)
                {
                    // Client sent graceful close - remove from dictionary before disposing
                    _availableWorkerSockets.TryRemove(socketData.ConnectionId, out _);
                    OnClientDisconnected(socketData.ConnectionId);
                    socketData.MCurrentSocket.Close();
                    socketData.MCurrentSocket.Dispose();
                    return;
                }

                var message = new string(chars).Replace("\0", "");

                if (string.IsNullOrEmpty(message))
                {
                    // Empty message - continue waiting for data
                    WaitForData(socketData.MCurrentSocket, socketData.ConnectionId, socketData);
                    return;
                }

                if (!message.EndsWith(ProtocolConstants.MessageTerminator, StringComparison.Ordinal))
                {
                    socketData.Partial.Append(message);
                    WaitForData(socketData.MCurrentSocket, socketData.ConnectionId, socketData);
                    return;
                }

                if (socketData.Partial.Length > 0)
                {
                    message = socketData.Partial.Append(message).ToString();
                    socketData.Partial.Clear();
                }

                _handler.ProcessIncomingMessage(message, socketData.ConnectionId);

                // Continue the waiting for data on the Socket.
                WaitForData(socketData.MCurrentSocket, socketData.ConnectionId, socketData);
            }
            catch (ObjectDisposedException)
            {
                OnClientDisconnected(connectionId);
                _logger.Debug("OnDataReceived: Socket has been closed");
            }
            catch (SocketException se)
            {
                if (se.ErrorCode == NetworkConstants.SocketErrorConnectionReset) // Error code for Connection reset by peer
                {
                    _availableWorkerSockets.TryRemove(connectionId, out _);
                    OnClientDisconnected(connectionId);
                }
                else
                {
                    _logger.LogError(se, "On DataReceive");
                }
            }
        }

        /// <summary>
        ///     Helper method to send a message to a specific socket with proper error handling
        /// </summary>
        private static void SendMessageToSocket(Socket socket, string message)
        {
            if (socket?.Connected != true || string.IsNullOrEmpty(message))
                return;

            var messageBytes = Encoding.UTF8.GetBytes(message);
            var totalBytes = new byte[messageBytes.Length + NewLineBytes.Length];

            Array.Copy(messageBytes, 0, totalBytes, 0, messageBytes.Length);
            Array.Copy(NewLineBytes, 0, totalBytes, messageBytes.Length, NewLineBytes.Length);

            socket.Send(totalBytes);
        }

        private void RemoveDeadSocket(string connectionId)
        {
            // Match original behavior - just remove and dispose without shutdown
            // Shutdown can throw ObjectDisposedException if socket is already disposed
            if (_availableWorkerSockets.TryRemove(connectionId, out var worker))
            {
                worker?.Dispose();
            }
        }

        private bool IsClientAllowed(IPAddress ipAddress, string ipString)
        {
            if (ipAddress.Equals(IPAddress.Loopback) || ipAddress.Equals(IPAddress.IPv6Loopback))
                return true;

            switch (_userSettings.FilterSelection)
            {
                case FilteringSelection.Specific:
                    return _userSettings.IpAddressList.Any(source =>
                        string.Equals(ipString, source, StringComparison.Ordinal));

                case FilteringSelection.Range:
                    return IsInAllowedRange(ipString);

                default:
                    return true;
            }
        }

        private bool IsInAllowedRange(string ipString)
        {
            try
            {
                if (string.IsNullOrEmpty(ipString) || string.IsNullOrEmpty(_userSettings.BaseIp))
                    return false;

                var connectingAddress = ipString.Split(DotSeparator);
                var baseIp = _userSettings.BaseIp.Split(DotSeparator);

                if (connectingAddress.Length != 4 || baseIp.Length != 4)
                    return false;

                // Check first three octets match
                for (var i = 0; i < 3; i++)
                    if (connectingAddress[i] != baseIp[i])
                        return false;

                // Validate last octet is in allowed range
                if (!int.TryParse(connectingAddress[3], out var connectingAddressLowOctet) ||
                    !int.TryParse(baseIp[3], out var baseIpAddressLowOctet))
                    return false;

                return connectingAddressLowOctet >= baseIpAddressLowOctet &&
                       connectingAddressLowOctet <= _userSettings.LastOctetMax;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error validating IP range for {ipString}: {ex.Message}");
                return false;
            }
        }

        private void RejectConnection(Socket workerSocket, string ipString)
        {
            try
            {
                var rejectMessage = new SocketMessage(ProtocolConstants.NotAllowed, string.Empty).ToJsonString();
                SendMessageToSocket(workerSocket, rejectMessage);

                _logger.Debug($"Client {ipString} was rejected - IP not allowed");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error rejecting connection from {ipString}: {ex.Message}");
            }
            finally
            {
                try
                {
                    workerSocket?.Close();
                    workerSocket?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Error closing rejected socket from {ipString}: {ex.Message}");
                }

                // Continue accepting new connections
                if (_mainSocket != null && _isRunning)
                    try
                    {
                        _mainSocket.BeginAccept(OnClientConnect, null);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error restarting accept after rejection");
                    }
            }
        }
    }
}
