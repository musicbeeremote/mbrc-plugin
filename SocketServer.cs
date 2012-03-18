using System;
using System.Collections;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace MusicBeePlugin
{
    public sealed class SocketServer
    {
        // New Code Stuff
        private Socket m_mainSocket;
        private ArrayList m_workerSocketList;
        private int m_clientCount;
        public AsyncCallback pfnWorkerCallback;

        private static readonly SocketServer ServerInstance = new SocketServer();

        static SocketServer()
        {
            
        }

        private SocketServer()
        {
            // New Code Stuff
            m_clientCount = 0;
            m_workerSocketList = ArrayList.Synchronized(new ArrayList());
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
            if(m_mainSocket!=null)
            {
                m_mainSocket.Close();
            }

            for(int i = 0; i<m_workerSocketList.Count; i++)
            {
                Socket workerSocket = (Socket) m_workerSocketList[i];
                if (workerSocket == null) continue;
                workerSocket.Close();
                workerSocket = null;
            }

            return true;
        }   

        /// <summary>
        /// It starts the SocketServer.
        /// </summary>
        /// <returns></returns>
        public bool Start()
        {
            try
            {
                m_mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                // Create the listening socket.    
                IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, UserSettings.ListeningPort);
                // Bind to local IP address.
                m_mainSocket.Bind(ipLocal);
                // Start Listening.
                m_mainSocket.Listen(4);
                // Create the call back for any client connections.
                m_mainSocket.BeginAccept(new AsyncCallback(OnClientConnect), null);
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
            try
            {
                // Here we complete/end the BeginAccept asyncronus call
                // by calling EndAccept() - Which returns the reference
                // to a new Socket object.
                Socket workerSocket = m_mainSocket.EndAccept(ar);

                // Now increment the client count for this client
                //in a thread safe manner
                Interlocked.Increment(ref m_clientCount);

                // Add the workerSocket reference to our ArrayList.
                m_workerSocketList.Add(workerSocket);

                //Sendmsg to client

                // Let the worker Socket do the further processing 
                // for the just connected client.
                WaitForData(workerSocket, m_clientCount);

                // Since the main Socket is now free, it can go back and
                // wait for the other clients who are attempting to connect
                m_mainSocket.BeginAccept(new AsyncCallback(OnClientConnect), null);
                

            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("OnClientConnection: Socket has been closed\n");
                
            }
            catch(SocketException se)
            {
                ErrorHandler.LogError(se);
            }
        }

        public class SocketPacket
        {
           // Constructor which takes a Socket and a client number
            public SocketPacket(Socket socket, int clientNumber)
            {
                m_currentSocket = socket;
                m_clientNumber = clientNumber;
            }

            public Socket m_currentSocket;
            public int m_clientNumber;
            // Buffer to store the data sent by the client
            public byte[] dataBuffer = new byte[1024];
        }

        // Start waiting for data from the client
        public void WaitForData(Socket socket, int clientNumber)
        {
            try
            {
                if(pfnWorkerCallback == null)
                {
                    // Specify the call back function which is to be
                    // invoked when ther is any write activity by the
                    // connected client.
                    pfnWorkerCallback = new AsyncCallback(OnDataReceived);
                }

                SocketPacket socketPacket = new SocketPacket(socket,clientNumber);

                socket.BeginReceive(socketPacket.dataBuffer, 0, socketPacket.dataBuffer.Length, SocketFlags.None,
                                    pfnWorkerCallback, socketPacket);

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

                int iRx = socketData.m_currentSocket.EndReceive(ar);
                char[] chars = new char[iRx + 1];

                System.Text.Decoder decoder = System.Text.Encoding.UTF8.GetDecoder();

                int charLen = decoder.GetChars(socketData.dataBuffer, 0, iRx, chars, 0);

                String szData = new string(chars);

                ProtocolHandler.Instance.ProcessIncomingMessage(szData);

                // Continue the waiting for data on the Socket.
                WaitForData(socketData.m_currentSocket, socketData.m_clientNumber);
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("OnDataReceived: Socket has been closed\n");
            }
            catch(SocketException se)
            {
                if(se.ErrorCode == 10054) // Error code for Connection reset by peer
                {
                    m_workerSocketList[socketData.m_clientNumber - 1] = null;
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
            Socket workerSocket = (Socket) m_workerSocketList[clientNumber - 1];
            workerSocket.Send(data);
        }

        public void Send(string message)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            for (int i = 0; i < m_workerSocketList.Count; i++)
            {
                Socket workerSocket = (Socket)m_workerSocketList[i];
                if (workerSocket.Connected)
                {
                    workerSocket.Send(data);
                }
            }

        }

    }
}