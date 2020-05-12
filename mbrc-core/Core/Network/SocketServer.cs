using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Settings;
using MusicBeeRemote.Core.Threading;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json;
using NLog;
using TinyMessenger;
using Timer = System.Timers.Timer;

namespace MusicBeeRemote.Core.Network
{
    /// <summary>
    /// The socket server.
    /// </summary>
    public sealed class SocketServer : IDisposable
    {
        private const string NewLine = "\r\n";

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ProtocolHandler _handler;
        private readonly ConcurrentDictionary<string, Socket> _availableWorkerSockets;
        private readonly TaskFactory _factory;
        private readonly ITinyMessengerHub _hub;
        private readonly Authenticator _auth;
        private readonly PersistenceManager _settings;

        /// <summary>
        /// The main socket. This is the Socket that listens for new client connections.
        /// </summary>
        private Socket _mainSocket;

        /// <summary>
        /// The worker callback.
        /// </summary>
        private AsyncCallback _workerCallback;

        private bool _isRunning;
        private bool _isDisposed;
        private Timer _pingTimer;

        public SocketServer(
            ProtocolHandler handler,
            ITinyMessengerHub hub,
            Authenticator auth,
            PersistenceManager settings)
        {
            _handler = handler;
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _auth = auth;
            _settings = settings;
            _availableWorkerSockets = new ConcurrentDictionary<string, Socket>();
            TaskScheduler scheduler = new LimitedTaskScheduler(2);
            _factory = new TaskFactory(scheduler);

            _hub.Subscribe<RestartSocketEvent>(eEvent => RestartSocket());
            _hub.Subscribe<ForceClientDisconnect>(eEvent => DisconnectSocket(eEvent.ConnectionId));
            _hub.Subscribe<BroadcastEventAvailable>(eEvent => Broadcast(eEvent.BroadcastEvent));
            _hub.Subscribe<PluginResponseAvailableEvent>(eEvent => Send(eEvent.Message, eEvent.ConnectionId));
        }

        ~SocketServer()
        {
            Dispose(false);
        }

        /// <summary>
        /// Sets a value indicating whether the socket server is running or not. (not completely reliable).
        /// </summary>
        private bool IsRunning
        {
            set
            {
                _isRunning = value;
                _hub.Publish(new SocketStatusChanged(_isRunning));
            }
        }

        /// <summary>
        /// It starts the SocketServer.
        /// </summary>
        public void Start()
        {
            _logger.Debug($"Socket starts listening on port: {_settings.UserSettingsModel.ListeningPort}");
            try
            {
                _mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // Create the listening socket.
                var ipLocal = new IPEndPoint(IPAddress.Any, Convert.ToInt32(_settings.UserSettingsModel.ListeningPort));

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

        /// <summary>
        /// It stops the SocketServer.
        /// </summary>
        public void Terminate()
        {
            _logger.Debug("Stopping socket service");
            try
            {
                _mainSocket?.Close();

                foreach (var wSocket in _availableWorkerSockets.Values)
                {
                    if (wSocket == null)
                    {
                        continue;
                    }

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
        /// Disposes anything Related to the socket server at the end of life of the Object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                _mainSocket?.Close();
                _mainSocket?.Dispose();
                _pingTimer.Dispose();
            }

            _isDisposed = true;
        }

        /// <summary>
        /// Finds an active connection with that matches the specified connection id, closes
        /// the connection and disposes the worker socket.
        /// </summary>
        /// <param name="connectionId">The id of the connection we want to disconnect.</param>
        private void DisconnectSocket(string connectionId)
        {
            try
            {
                if (!_availableWorkerSockets.TryRemove(connectionId, out var workerSocket))
                {
                    return;
                }

                workerSocket.Close();
                workerSocket.Dispose();
                _hub.Publish(new ClientDisconnectedEvent(connectionId));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "While disconnecting a socket");
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
        private void RestartSocket()
        {
            Terminate();
            Start();
        }

        // this is the call back function,
        private void OnClientConnect(IAsyncResult ar)
        {
            try
            {
                if (_mainSocket == null)
                {
                    return;
                }

                // Here we complete/end the BeginAccept asynchronous call
                // by calling EndAccept() - Which returns the reference
                // to a new Socket object.
                var workerSocket = _mainSocket.EndAccept(ar);

                // Validate If client should connect.
                var ipAddress = ((IPEndPoint)workerSocket.RemoteEndPoint).Address;
                var ipString = ipAddress.ToString();

                var isAllowed = false;
                switch (_settings.UserSettingsModel.FilterSelection)
                {
                    case FilteringSelection.Specific:
                        foreach (var source in _settings.UserSettingsModel.IpAddressList)
                        {
                            if (string.Compare(ipString, source, StringComparison.Ordinal) == 0)
                            {
                                isAllowed = true;
                            }
                        }

                        break;

                    case FilteringSelection.Range:
                        var settings = _settings.UserSettingsModel;
                        isAllowed = RangeChecker.AddressInRange(ipString, settings.BaseIp, settings.LastOctetMax);
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
                    workerSocket.Send(
                        Encoding.UTF8.GetBytes(new SocketMessage(Constants.NotAllowed, string.Empty).ToJsonString()));
                    workerSocket.Close();
                    _logger.Debug($"Client {ipString} was force disconnected IP was not in the allowed addresses");
                    _mainSocket.BeginAccept(OnClientConnect, null);
                    return;
                }

                var connectionId = IdGenerator.GetUniqueKey();

                if (!_availableWorkerSockets.TryAdd(connectionId, workerSocket))
                {
                    return;
                }

                // Inform the the Protocol Handler that a new Client has been connected, prepare for handshake.
                _hub.Publish(new ClientConnectedEvent(ipAddress, connectionId));

                // Let the worker Socket do the further processing
                // for the just connected client.
                WaitForData(workerSocket, connectionId);
            }
            catch (ObjectDisposedException)
            {
                _logger.Debug("OnClientConnection: Socket has been closed");
            }
            catch (SocketException se)
            {
                _logger.Debug(se, "On client connect");
            }
            catch (Exception ex)
            {
                _logger.Debug($"OnClientConnect Exception : {ex.Message}");
            }
            finally
            {
                try
                {
                    // Since the main Socket is now free, it can go back and
                    // wait for the other clients who are attempting to connect
                    _mainSocket?.BeginAccept(OnClientConnect, null);
                }
                catch (Exception e)
                {
                    _logger.Debug($"OnClientConnect Exception : {e.Message}");
                }
            }
        }

        // Start waiting for data from the client
        private void WaitForData(Socket socket, string connectionId, SocketPacket packet = null)
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

                var socketPacket = packet ?? new SocketPacket(socket, connectionId);

                socket.BeginReceive(
                    socketPacket.GetDataBuffer(),
                    0,
                    socketPacket.GetDataBuffer().Length,
                    SocketFlags.None,
                    _workerCallback,
                    socketPacket);
            }
            catch (SocketException se)
            {
                _logger.Debug(se, "On WaitForData");
                if (se.ErrorCode == 10053)
                {
                    _hub.Publish(new ClientDisconnectedEvent(connectionId));
                }
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

                var iRx = socketData.WorkerSocket.EndReceive(ar);
                var chars = new char[iRx + 1];

                var decoder = Encoding.UTF8.GetDecoder();

                decoder.GetChars(socketData.GetDataBuffer(), 0, iRx, chars, 0);
                if (chars.Length == 1 && chars[0] == 0)
                {
                    socketData.WorkerSocket.Close();
                    socketData.WorkerSocket.Dispose();
                    return;
                }

                var message = new string(chars).Replace("\0", string.Empty);

                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                if (!message.EndsWith("\r\n", StringComparison.InvariantCultureIgnoreCase))
                {
                    socketData.Partial.Append(message);
                    WaitForData(socketData.WorkerSocket, socketData.ConnectionId, socketData);
                    return;
                }

                if (socketData.Partial.Length > 0)
                {
                    message = socketData.Partial.Append(message).ToString();
                    socketData.Partial.Clear();
                }

                _factory.StartNew(() => _handler.ProcessIncomingMessage(message, socketData.ConnectionId));

                // Continue the waiting for data on the Socket.
                WaitForData(socketData.WorkerSocket, socketData.ConnectionId, socketData);
            }
            catch (ObjectDisposedException)
            {
                _hub.Publish(new ClientDisconnectedEvent(connectionId));
                _logger.Debug("OnDataReceived: Socket has been closed");
            }
            catch (SocketException se)
            {
                // Error code for Connection reset by peer
                if (se.ErrorCode == 10054)
                {
                    if (_availableWorkerSockets.ContainsKey(connectionId))
                    {
                        _availableWorkerSockets.TryRemove(connectionId, out _);
                    }

                    _hub.Publish(new ClientDisconnectedEvent(connectionId));
                }
                else
                {
                    _logger.Error(se, "On DataReceive");
                }
            }
        }

        /// <summary>
        /// Sends a message to a specific client with a specific connection id.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="connectionId">The id of the connection that will receive the message.</param>
        private void Send(SocketMessage message, string connectionId)
        {
            var serializedMessage = JsonConvert.SerializeObject(message);
            if (message.NewLineTerminated)
            {
                serializedMessage += NewLine;
            }

            if (connectionId.Equals("all", StringComparison.InvariantCultureIgnoreCase))
            {
                Send(serializedMessage);
                return;
            }

            _logger.Debug($"sending-{connectionId}:{serializedMessage}");

            try
            {
                var data = Encoding.UTF8.GetBytes(serializedMessage + NewLine);
                if (_availableWorkerSockets.TryGetValue(connectionId, out var wSocket))
                {
                    wSocket.Send(data);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "While sending message to specific client");
            }
        }

        private void RemoveDeadSocket(string connectionId)
        {
            _availableWorkerSockets.TryRemove(connectionId, out var worker);
            worker?.Dispose();
            _hub.Publish(new ClientDisconnectedEvent(connectionId));
        }

        private void Broadcast(BroadcastEvent broadcastEvent)
        {
            _logger.Debug($"broadcasting message {broadcastEvent}");

            try
            {
                foreach (var key in _availableWorkerSockets.Keys)
                {
                    if (!_availableWorkerSockets.TryGetValue(key, out var worker))
                    {
                        continue;
                    }

                    var isConnected = worker != null && worker.Connected;
                    if (!isConnected)
                    {
                        RemoveDeadSocket(key);
                        _hub.Publish(new ClientDisconnectedEvent(key));
                    }

                    if (!isConnected || !_auth.CanConnectionReceive(key) ||
                        !_auth.IsConnectionBroadcastEnabled(key))
                    {
                        continue;
                    }

                    var clientProtocol = _auth.ClientProtocolVersion(key);
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
        /// Sends a message to all the connections that are in broadcast mode.
        /// The connections that are not in broadcast mode will be skipped.
        /// </summary>
        /// <param name="message">The message that will be send through the socket connection.</param>
        private void Send(string message)
        {
            _logger.Debug($"sending-all: {message}");

            try
            {
                var data = Encoding.UTF8.GetBytes(message + NewLine);

                foreach (var key in _availableWorkerSockets.Keys)
                {
                    if (!_availableWorkerSockets.TryGetValue(key, out var worker))
                    {
                        continue;
                    }

                    var isConnected = worker != null && worker.Connected;
                    if (!isConnected)
                    {
                        RemoveDeadSocket(key);
                        _hub.Publish(new ClientDisconnectedEvent(key));
                    }

                    if (isConnected && _auth.CanConnectionReceive(key) && _auth.IsConnectionBroadcastEnabled(key))
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
    }
}
