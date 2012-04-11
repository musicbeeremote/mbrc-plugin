using System;
using System.Collections;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using MusicBeePlugin.Events;
using MusicBeePlugin.Settings;

namespace MusicBeePlugin
{
    public sealed class SocketServer
    {
        // New Code Stuff
        private Socket _mMainSocket;
        private readonly ArrayList _mWorkerSocketList;
        private int _mClientCount;
        public AsyncCallback PfnWorkerCallback;

        private static readonly SocketServer ServerInstance = new SocketServer();

        static SocketServer()
        {
            
        }

        private SocketServer()
        {
            // New Code Stuff
            _mClientCount = 0;
            _mWorkerSocketList = ArrayList.Synchronized(new ArrayList());
            Messenger.Instance.DisconnectClient += HandleDisconnectClient;
        }

        private void HandleDisconnectClient(object sender, MessageEventArgs e)
        {
            Socket workerSocket = (Socket)_mWorkerSocketList[e.ClientId-1];
            workerSocket.Close();
            workerSocket = null;
        }

        /// <summary>
        /// Gives access to the SocketServer single instance.
        /// </summary>
        public static SocketServer Instance
        {
            get { return ServerInstance; }
        }

        /// <summary>
        /// It stops the SocketServer.
        /// </summary>
        /// <returns></returns>
        public bool Stop()
        {
            try
            {
                if (_mMainSocket != null)
                {
                    _mMainSocket.Close();
                }

                foreach (object t in _mWorkerSocketList)
                {
                    Socket workerSocket = (Socket)t;
                    if (workerSocket == null) continue;
                    workerSocket.Close();
                    workerSocket = null;
                }
                _mMainSocket = null;
                GC.Collect();

                return true;
            }
            catch (Exception ex)
            {
               ErrorHandler.LogError(ex);
                return false;
            }
        }   

        /// <summary>
        /// It starts the SocketServer.
        /// </summary>
        /// <returns></returns>
        public bool Start()
        {
            try
            {
                _mMainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                // Create the listening socket.    
                IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, UserSettings.Settings.ListeningPort);
                // Bind to local IP address.
                _mMainSocket.Bind(ipLocal);
                // Start Listening.
                _mMainSocket.Listen(4);
                // Create the call back for any client connections.
                _mMainSocket.BeginAccept(OnClientConnect, null);
                return true;
            }
            catch (SocketException se)
            {
                ErrorHandler.LogError(se);
                return false;
            }

        }

        // this is the call back function,
        private void OnClientConnect(IAsyncResult ar)
        {
            if(ar==null)
                return;
            try
            {
                // Here we complete/end the BeginAccept asynchronous call
                // by calling EndAccept() - Which returns the reference
                // to a new Socket object.
                Socket workerSocket = _mMainSocket.EndAccept(ar);

                // Validate If client should connect.
                string address = ((IPEndPoint) workerSocket.RemoteEndPoint).Address.ToString();
                Debug.WriteLine(address);
                if(string.Compare(address,"192.168.110.103",StringComparison.Ordinal)!=0)
                {
                    workerSocket.Close();
                    Debug.WriteLine("Force Disconnected not valid range");
                    return;
                }

                // Now increment the client count for this client
                //in a thread safe manner
                Interlocked.Increment(ref _mClientCount);

                // Add the workerSocket reference to our ArrayList.
                _mWorkerSocketList.Add(workerSocket);

                // Inform the the Protocol Handler that a new Client has been connected, prepare for handshake.
                Messenger.Instance.OnClientConnected(new MessageEventArgs(_mClientCount));

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
                Debug.WriteLine("OnClientConnection: Socket has been closed\n");
                
            }
            catch(SocketException se)
            {
                ErrorHandler.LogError(se);
            }
            catch(Exception ex)
            {
                Debug.WriteLine("Teh ex" + ex.Message);
                ErrorHandler.LogError(ex);
            }
        }

        public class SocketPacket
        {
           // Constructor which takes a Socket and a client number
            public SocketPacket(Socket socket, int clientNumber)
            {
                MCurrentSocket = socket;
                MClientNumber = clientNumber;
            }

            public Socket MCurrentSocket;
            public int MClientNumber;
            // Buffer to store the data sent by the client
            public byte[] DataBuffer = new byte[1024];
        }

        // Start waiting for data from the client
        public void WaitForData(Socket socket, int clientNumber)
        {
            try
            {
                if(PfnWorkerCallback == null)
                {
                    // Specify the call back function which is to be
                    // invoked when ther is any write activity by the
                    // connected client.
                    PfnWorkerCallback = OnDataReceived;
                }

                SocketPacket socketPacket = new SocketPacket(socket,clientNumber);

                socket.BeginReceive(socketPacket.DataBuffer, 0, socketPacket.DataBuffer.Length, SocketFlags.None,
                                    PfnWorkerCallback, socketPacket);

            }
            catch (SocketException se)
            {
               ErrorHandler.LogError(se);
            }
        }

        // This is the call back function which will be invoked when the socket
        // detects any client writing of data on the stream
        public void OnDataReceived(IAsyncResult ar)
        {
            SocketPacket socketData = (SocketPacket) ar.AsyncState;
            try
            {
                // Complete the BeginReceive() asynchronus call by EndReceive() method
                // which will return the number of characters writter to the stream
                // by the client.

                int iRx = socketData.MCurrentSocket.EndReceive(ar);
                char[] chars = new char[iRx + 1];

                System.Text.Decoder decoder = System.Text.Encoding.UTF8.GetDecoder();

                decoder.GetChars(socketData.DataBuffer, 0, iRx, chars, 0);

                String message = new string(chars);

                if(String.IsNullOrEmpty(message))
                    return;
                ProtocolHandler.Instance.ProcessIncomingMessage(message, socketData.MClientNumber);

                // Continue the waiting for data on the Socket.
                WaitForData(socketData.MCurrentSocket, socketData.MClientNumber);
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("OnDataReceived: Socket has been closed\n");
            }
            catch(SocketException se)
            {
                if(se.ErrorCode == 10054) // Error code for Connection reset by peer
                {
                    _mWorkerSocketList[socketData.MClientNumber - 1] = null;
                    Messenger.Instance.OnClientDisconnected(new MessageEventArgs(socketData.MClientNumber));
                }
                else
                {
                    ErrorHandler.LogError(se);    
                }
                
            }
        }

        public void Send(string message, int clientNumber)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            Socket workerSocket = (Socket) _mWorkerSocketList[clientNumber - 1];
            workerSocket.Send(data);
        }

        public void Send(string message)
        {
            try
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
                for (int i = 0; i < _mWorkerSocketList.Count; i++)
                {
                    Socket workerSocket = (Socket)_mWorkerSocketList[i];
                    if (workerSocket != null && workerSocket.Connected && ProtocolHandler.Instance.IsClientAuthenticated(i+1))
                    {
                        workerSocket.Send(data);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError(ex);
            }

        }

    }
}