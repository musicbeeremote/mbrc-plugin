using System.Collections.Concurrent;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
    using Error;
    using Settings;
    using Utilities;
    using Events;

    /// <summary>
    /// The socket server.
    /// </summary>
    public sealed class SocketServer : IDisposable
    {
        private readonly ProtocolHandler handler;

        private readonly ConcurrentDictionary<string, Socket> availableWorkerSockets;  

        /// <summary>
        /// The main socket. This is the Socket that listens for new client connections.
        /// </summary>
        private Socket mainSocket;

        private readonly static SocketServer Server = new SocketServer();

        /// <summary>
        /// The worker callback.
        /// </summary>
        private AsyncCallback workerCallback;


        /// <summary>
        /// Returns the Instance of the signleton socketserver
        /// </summary>
        public static SocketServer Instance
        {
            get { return Server; }
        }

        /// <summary>
        /// 
        /// </summary>
        private SocketServer()
        {
            handler = new ProtocolHandler();
            IsRunning = false;
            availableWorkerSockets = new ConcurrentDictionary<string, Socket>();
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsRunning { get; private set; }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientId"> </param>
        public void KickClient(string clientId)
        {
            try
            {
                Socket workerSocket;
                if (!availableWorkerSockets.TryRemove(clientId, out workerSocket)) return;
                workerSocket.Close();
                workerSocket.Dispose();
                
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
            }
        }

        /// <summary>
        /// It stops the SocketServer.
        /// </summary>
        /// <returns></returns>
        public void Stop()
        {
            try
            {
                if (mainSocket != null)
                {
                    mainSocket.Close();
                }

                foreach (Socket wSocket in availableWorkerSockets.Values)
                {
                    if (wSocket == null) continue;
                    wSocket.Close();
                    wSocket.Dispose();
                }
                mainSocket = null;
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
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
            try
            {
                mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                // Create the listening socket.    
                IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, Convert.ToInt32(UserSettings.Instance.ListeningPort));
                // Bind to local IP address.
                mainSocket.Bind(ipLocal);
                // Start Listening.
                mainSocket.Listen(4);
                // Create the call back for any client connections.
                mainSocket.BeginAccept(OnClientConnect, null);
                IsRunning = true;
            }
            catch (SocketException se)
            {
#if DEBUG
                ErrorHandler.LogError(se);
#endif
            }
        }

        // this is the call back function,
        private void OnClientConnect(IAsyncResult ar)
        {
            try
            {
                // Here we complete/end the BeginAccept asynchronous call
                // by calling EndAccept() - Which returns the reference
                // to a new Socket object.
                Socket workerSocket = mainSocket.EndAccept(ar);

                // Validate If client should connect.
                IPAddress ipAddress = ((IPEndPoint) workerSocket.RemoteEndPoint).Address;
                string ipString = ipAddress.ToString();
                
                bool isAllowed = false;
                switch (UserSettings.Instance.FilterSelection)
                {
                    case FilteringSelection.Specific:
                        foreach (string source in UserSettings.Instance.IpAddressList)
                        {
                            if (string.Compare(ipString, source, StringComparison.Ordinal) == 0)
                            {
                                isAllowed = true;
                            }
                        }
                        break;
                    case FilteringSelection.Range:
                        string[] connectingAddress = ipString.Split(".".ToCharArray(),
                                                                   StringSplitOptions.RemoveEmptyEntries);
                        string[] baseIp = UserSettings.Instance.BaseIp.Split(".".ToCharArray(),
                                                                             StringSplitOptions.RemoveEmptyEntries);
                        if (connectingAddress[0] == baseIp[0] && connectingAddress[1] == baseIp[1] &&
                            connectingAddress[2] == baseIp[2])
                        {
                            int connectingAddressLowOctet;
                            int baseIpAddressLowOctet;
                            int.TryParse(connectingAddress[3], out connectingAddressLowOctet);
                            int.TryParse(baseIp[3], out baseIpAddressLowOctet);
                            if (connectingAddressLowOctet >= baseIpAddressLowOctet &&
                                baseIpAddressLowOctet <= UserSettings.Instance.LastOctetMax)
                            {
                                isAllowed = true;
                            }
                        }
                        break;
                    default:
                        isAllowed = true;
                        break;
                }
                if (!isAllowed)
                {
                    workerSocket.Send(System.Text.Encoding.UTF8.GetBytes("<notAllowed/>\0\r\n"));
                    workerSocket.Close();
#if DEBUG
                    Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : Force Disconnected not valid range\n");
#endif
                    mainSocket.BeginAccept(OnClientConnect, null);
                    return;
                }

                string clientId = IdGenerator.GetUniqueKey();

                if(availableWorkerSockets.TryAdd(clientId, workerSocket))
                {
                    // Inform the the Protocol Handler that a new Client has been connected, prepare for handshake.
                    EventBus.FireEvent(new MessageEvent(EventType.ActionClientConnected, string.Empty, clientId));

                    // Let the worker Socket do the further processing 
                    // for the just connected client.
                    WaitForData(workerSocket, clientId);
                }
                
                // Since the main Socket is now free, it can go back and
                // wait for the other clients who are attempting to connect
                 mainSocket.BeginAccept(OnClientConnect, null);
            }
            catch (ObjectDisposedException)
            {
#if DEBUG
                Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : OnClientConnection: Socket has been closed\n");
#endif
            }
            catch (SocketException se)
            {
#if DEBUG
                ErrorHandler.LogError(se);
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : OnClientConnect Exception : " + ex.Message + "\n");
                ErrorHandler.LogError(ex);
#endif
            }
        }

        // Start waiting for data from the client
        private void WaitForData(Socket socket, string clientId)
        {
            try
            {
                if (workerCallback == null)
                {
                    // Specify the call back function which is to be
                    // invoked when there is any write activity by the
                    // connected client.
                    workerCallback = OnDataReceived;
                }

                SocketPacket socketPacket = new SocketPacket(socket, clientId);

                socket.BeginReceive(socketPacket.DataBuffer, 0, socketPacket.DataBuffer.Length, SocketFlags.None,
                                    workerCallback, socketPacket);
            }
            catch (SocketException se)
            {
                if (se.ErrorCode != 10053)
                {
#if DEBUG
                    ErrorHandler.LogError(se);
#endif
                }
                else
                {
                    EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, clientId));
                }
            }
        }

        // This is the call back function which will be invoked when the socket
        // detects any client writing of data on the stream
        private void OnDataReceived(IAsyncResult ar)
        {
            string clientId = String.Empty;
            try
            {
                SocketPacket socketData = (SocketPacket) ar.AsyncState;
                // Complete the BeginReceive() asynchronus call by EndReceive() method
                // which will return the number of characters written to the stream
                // by the client.

                clientId = socketData.ClientId;

                int iRx = socketData.MCurrentSocket.EndReceive(ar);
                char[] chars = new char[iRx + 1];

                System.Text.Decoder decoder = System.Text.Encoding.UTF8.GetDecoder();

                decoder.GetChars(socketData.DataBuffer, 0, iRx, chars, 0);
                if(chars.Length==1&&chars[0]==0)
                {
                    socketData.MCurrentSocket.Close();
                    socketData.MCurrentSocket.Dispose();
                    return;
                }
                String message = new string(chars);

                if (String.IsNullOrEmpty(message))
                    return;

#if DEBUG
                Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : message Received : " + message);
#endif
                handler.ProcessIncomingMessage(message,socketData.ClientId);
                
                // Continue the waiting for data on the Socket.
                WaitForData(socketData.MCurrentSocket, socketData.ClientId);
            }
            catch (ObjectDisposedException)
            {
                EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, clientId));
#if DEBUG
                Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : OnDataReceived: Socket has been closed\n");
#endif
            }
            catch (SocketException se)
            {
                if (se.ErrorCode == 10054) // Error code for Connection reset by peer
                {
                    Socket deadSocket;
                    if (availableWorkerSockets.ContainsKey(clientId))
                        availableWorkerSockets.TryRemove(clientId, out deadSocket);
                    EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, clientId));
                }
                else
                {
#if DEBUG
                    ErrorHandler.LogError(se);
#endif
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
            if(clientId.Equals("all"))
            {
                Send(message);
                return;
            }
            try
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(message + "\r\n");
                Socket wSocket;
                if(availableWorkerSockets.TryGetValue(clientId,out wSocket))
                {
                    wSocket.Send(data);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
            }
        }

        private void RemoveDeadSocket(string clientId)
        {
            Socket worker;
            availableWorkerSockets.TryRemove(clientId, out worker);
            if(worker!=null)
            {
                worker.Dispose();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        public void Send(string message)
        {
            try
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(message);

                foreach (string key in availableWorkerSockets.Keys)
                {
                    Socket worker;
                    if (!availableWorkerSockets.TryGetValue(key, out worker)) continue;
                    bool isConnected = (worker != null && worker.Connected);
                    if(!isConnected)
                    {
                        RemoveDeadSocket(key);
                        EventBus.FireEvent(new MessageEvent(EventType.ActionClientDisconnected, string.Empty, key));
                    }
                    if (isConnected && Authenticator.IsClientAuthenticated(key))
                    {
                        worker.Send(data);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
            }
        }

        /// <summary>
        /// Disposes anything Related to the socket server at the end of life of the Object.
        /// </summary>
        public void Dispose()
        {
            mainSocket.Dispose();
        }
    }
}