using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace MusicBeePlugin
{
    public sealed class SocketServer
    {
        private static bool _isStopping;
        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static ManualResetEvent _clientConnected;

        private static Socket _clientSocket;

        private static readonly SocketServer ServerInstance = new SocketServer();

        static SocketServer()
        {
            //Empty Constructor
        }

        private SocketServer()
        {
            //Empty Constructor   
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
            _isStopping = true;
            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                _listenerThread.Abort();
            }
            _listenerThread = null;
            if (_listener != null)
            {
                _listener.Stop();
                _listener = null;
            }
            return true;
        }

        /// <summary>
        /// It starts the SocketServer.
        /// </summary>
        /// <returns></returns>
        public bool Start()
        {
            _isStopping = false;
            try
            {
                _listener = new TcpListener(IPAddress.Any, Int32.Parse(UserSettings.ListeningPort));
                _listener.Start();
                _clientConnected = new ManualResetEvent(false);
                _listenerThread = new Thread(ListenForClients) {IsBackground = true, Priority = ThreadPriority.Lowest};
                _listenerThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError(ex);
                return false;
            }
        }

        /// <summary>
        /// Listens for clients.
        /// </summary>
        /// <returns></returns>
        private static void ListenForClients()
        {
            do
            {
                try
                {
                    _clientConnected.Reset();
                    _listener.BeginAcceptSocket(AcceptSocketCallback, null);
                    _clientConnected.WaitOne();
                }
                catch
                {
                    Debug.WriteLine("Listener exception");
                }
            } while (!_isStopping);
        }

        public void Send(string data)
        {
            byte[] byteData = System.Text.Encoding.UTF8.GetBytes(data);
            _clientSocket.Send(byteData);
        }

        private static void AcceptSocketCallback(IAsyncResult result)
        {
            if (_isStopping) return;
            try
            {
                _clientSocket = _listener.EndAcceptSocket(result);
                //string address = ((IPEndPoint) _clientSocket.RemoteEndPoint).Address.ToString();
                //Send(_clientSocket,String.Format("<MusicBeeClientIP>{0}</MusicBeeClientIP>\0", address));
                byte[] buffer = new byte[_clientSocket.ReceiveBufferSize];
                bool connectionClosing = false;
                int count = 0;

                do
                {
                    int eocIndex = -1;
                    try
                    {
                        do
                        {
                            if (_clientSocket.Poll(-1, SelectMode.SelectRead))
                            {
                                int bytesRead = _clientSocket.Receive(buffer, count,
                                                                      _clientSocket.ReceiveBufferSize - count,
                                                                      SocketFlags.None);
                                if (bytesRead == 0)
                                {
                                    connectionClosing = true;
                                    break;
                                }
                                count += bytesRead;
                                eocIndex = Array.LastIndexOf<byte>(buffer, 10, count - 1);
                            }
                        } while ((count < _clientSocket.ReceiveBufferSize) && (eocIndex == -1));
                    }
                    catch
                    {
                        connectionClosing = true;
                    }

                    if (_isStopping) return;
                    ProtocolHandler.Instance.ProcessIncomingMessage(count == 0
                                                                        ? ""
                                                                        : System.Text.Encoding.UTF8.GetString(buffer, 0,
                                                                                                              count).
                                                                              Replace("\r\n", ""));
                    if (eocIndex == -1 || eocIndex == count - 1)
                    {
                        count = 0;
                    }
                    else
                    {
                        int remainder = count - (eocIndex + 1);
                        Array.Copy(buffer, eocIndex + 1, buffer, 0, remainder);
                        count = remainder;
                    }
                } while (!connectionClosing);
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError(ex);
            }
            finally
            {
                if (_clientSocket != null)
                {
                    try
                    {
                        _clientSocket.Close();
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.LogError(ex);
                    }
                    _clientSocket = null;
                }
                _clientConnected.Set();
            }
        }
    }
}