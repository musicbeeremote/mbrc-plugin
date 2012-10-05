using System;
using System.Collections;
using System.Globalization;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using MusicBeePlugin.AndroidRemote.Error;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class SocketServer : IDisposable
    {
        private bool _isRunning;
        private Socket _mMainSocket;
        private readonly ArrayList _mWorkerSocketList;
        private int _mClientCount;
        private AsyncCallback _pfnWorkerCallback;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<MessageEventArgs> ClientConnected;
        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<MessageEventArgs> ClientDisconnected;
 
        private void OnClientConnected(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = ClientConnected;
            if (handler != null) handler(this, e);
        }

        private void OnClientDisconnected(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = ClientDisconnected;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<MessageEventArgs> DataAvailable;
 
        private void OnDataAvailable(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = DataAvailable;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// 
        /// </summary>
        public SocketServer()
        {
            _isRunning = false;
            _mClientCount = 0;
            _mWorkerSocketList = ArrayList.Synchronized(new ArrayList());
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsRunning
        {
            get { return _isRunning; }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void HandleReplyAvailable(object sender, MessageEventArgs e)
        {
            if (e.ClientId == -1)
            {
                Send(e.Message);
            }
            else
            {
                Send(e.Message, e.ClientId);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void HandleDisconnectClient(object sender, MessageEventArgs e)
        {
            try
            {
                Socket workerSocket = (Socket) _mWorkerSocketList[e.ClientId - 1];
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
                if (_mMainSocket != null)
                {
                    _mMainSocket.Close();
                }

                foreach (object t in _mWorkerSocketList)
                {
                    Socket workerSocket = (Socket) t;
                    if (workerSocket == null) continue;
                    workerSocket.Close();
                    workerSocket.Dispose();
                }
                _mMainSocket = null;
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
            }
            finally
            {
                _isRunning = false;
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
                _mMainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                // Create the listening socket.    
                IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, UserSettings.SettingsModel.ListeningPort);
                // Bind to local IP address.
                _mMainSocket.Bind(ipLocal);
                // Start Listening.
                _mMainSocket.Listen(4);
                // Create the call back for any client connections.
                _mMainSocket.BeginAccept(OnClientConnect, null);
                _isRunning = true;
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
                Socket workerSocket = _mMainSocket.EndAccept(ar);

                // Validate If client should connect.
                IPAddress ipAddress = ((IPEndPoint) workerSocket.RemoteEndPoint).Address;
                string ipString = ipAddress.ToString();
                Debug.WriteLine(ipString);
                bool isAllowed = false;
                switch (UserSettings.SettingsModel.FilterSelection)
                {
                    case FilteringSelection.Specific:
                        foreach (string source in UserSettings.SettingsModel.IpAddressList)
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
                        string[] baseIp = UserSettings.SettingsModel.BaseIp.Split(".".ToCharArray(),
                                                                             StringSplitOptions.RemoveEmptyEntries);
                        if (connectingAddress[0] == baseIp[0] && connectingAddress[1] == baseIp[1] &&
                            connectingAddress[2] == baseIp[2])
                        {
                            int connectingAddressLowOctet;
                            int baseIpAddressLowOctet;
                            int.TryParse(connectingAddress[3], out connectingAddressLowOctet);
                            int.TryParse(baseIp[3], out baseIpAddressLowOctet);
                            if (connectingAddressLowOctet >= baseIpAddressLowOctet &&
                                baseIpAddressLowOctet <= UserSettings.SettingsModel.LastOctetMax)
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
                    _mMainSocket.BeginAccept(OnClientConnect, null);
                    return;
                }

                // Now increment the client count for this client
                //in a thread safe manner
                Interlocked.Increment(ref _mClientCount);

                // Add the workerSocket reference to our ArrayList.
                _mWorkerSocketList.Add(workerSocket);

                // Inform the the Protocol Handler that a new Client has been connected, prepare for handshake.
                OnClientConnected(new MessageEventArgs(_mClientCount));

                //Send msg to client

                // Let the worker Socket do the further processing 
                // for the just connected client.
                WaitForData(workerSocket, _mClientCount);

                // Since the main Socket is now free, it can go back and
                // wait for the other clients who are attempting to connect
                _mMainSocket.BeginAccept(OnClientConnect, null);
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
        private void WaitForData(Socket socket, int clientNumber)
        {
            try
            {
                if (_pfnWorkerCallback == null)
                {
                    // Specify the call back function which is to be
                    // invoked when there is any write activity by the
                    // connected client.
                    _pfnWorkerCallback = OnDataReceived;
                }

                SocketPacket socketPacket = new SocketPacket(socket, clientNumber);

                socket.BeginReceive(socketPacket.DataBuffer, 0, socketPacket.DataBuffer.Length, SocketFlags.None,
                                    _pfnWorkerCallback, socketPacket);
            }
            catch (SocketException se)
            {
                if (se.ErrorCode != 10053){
#if DEBUG
                    ErrorHandler.LogError(se);
#endif
                }
            }
        }

        // This is the call back function which will be invoked when the socket
        // detects any client writing of data on the stream
        private void OnDataReceived(IAsyncResult ar)
        {
            int clientNumber = 0;
            try
            {
                SocketPacket socketData = (SocketPacket) ar.AsyncState;
                clientNumber = socketData.MClientNumber;
                // Complete the BeginReceive() asynchronus call by EndReceive() method
                // which will return the number of characters written to the stream
                // by the client.

                int iRx = socketData.MCurrentSocket.EndReceive(ar);
                char[] chars = new char[iRx + 1];

                System.Text.Decoder decoder = System.Text.Encoding.UTF8.GetDecoder();

                decoder.GetChars(socketData.DataBuffer, 0, iRx, chars, 0);
                if(chars.Length==1&&(int)chars[0]==0)
                {
                    socketData.MCurrentSocket.Close();
                    socketData.MCurrentSocket.Dispose();
                    return;
                }
                String message = new string(chars);

                if (String.IsNullOrEmpty(message))
                    return;

#if DEBUG
                Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : Message Received : " + message);
#endif

                OnDataAvailable(new MessageEventArgs(message, socketData.MClientNumber));

                // Continue the waiting for data on the Socket.
                WaitForData(socketData.MCurrentSocket, socketData.MClientNumber);
            }
            catch (ObjectDisposedException)
            {
#if DEBUG
                Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : OnDataReceived: Socket has been closed\n");
#endif
            }
            catch (SocketException se)
            {
                if (se.ErrorCode == 10054) // Error code for Connection reset by peer
                {
                    _mWorkerSocketList[clientNumber - 1] = null;
                    OnClientDisconnected(new MessageEventArgs(clientNumber));
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
        /// <param name="clientNumber"></param>
        public void Send(string message, int clientNumber)
        {
            try
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
                Socket workerSocket = (Socket) _mWorkerSocketList[(clientNumber - 1) > 0 ? clientNumber - 1 : 0];
                workerSocket.Send(data);
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
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
                for (int i = 0; i < _mWorkerSocketList.Count; i++)
                {
                    Socket workerSocket = (Socket) _mWorkerSocketList[i];
                    if (workerSocket != null && workerSocket.Connected && Authenticator.IsClientAuthenticated(i + 1))
                    {
                        workerSocket.Send(data);
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

        public void Dispose()
        {
            _mMainSocket.Dispose();
        }
    }
}