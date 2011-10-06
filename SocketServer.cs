using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace MusicBeePlugin
{
    public class SocketServer
    {
        private static bool _isStopping;
        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static ManualResetEvent _clientConnected;

        private static Socket _clientSocket;
        private static Plugin _plugin;

        public SocketServer(Plugin plugin)
        {
            _plugin = plugin;
        }

        public static bool Stop()
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

        public static bool Start()
        {
            _isStopping = false;
            try
            {
                //if (UserConfiguration.SystemOptions.ExternalHosts == null || UserConfiguration.SystemOptions.ExternalHosts.Count == 0 || (UserConfiguration.SystemOptions.ExternalHosts.Count == 1 && UserConfiguration.SystemOptions.ExternalHosts(0) == "127.0.0.1"))
                //{
                //    _listener = new TcpListener(IPAddress.Parse("127.0.0.1"), UserConfiguration.SystemOptions.ServerPort);
                //}
                //else
                //{
                //    _listener = new TcpListener(IPAddress.Any, UserConfiguration.SystemOptions.ServerPort);
                //}
                _listener = new TcpListener(IPAddress.Any, 9999);
                _listener.Start();
                _clientConnected = new ManualResetEvent(false);
                _listenerThread = new Thread(ListenForClients);
                _listenerThread.IsBackground = true;
                _listenerThread.Priority = ThreadPriority.Lowest;
                _listenerThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                //ErrorHandler.LogError(ex);
                return false;
            }
        }

        private static void ListenForClients()
        {
            do
            {
                try
                {
                    _clientConnected.Reset();
                    _listener.BeginAcceptSocket(new AsyncCallback(AcceptSocketCallback), null);
                    _clientConnected.WaitOne();
                }
                catch
                {
                }
            } while (true);
        }

        private static void AcceptSocketCallback(IAsyncResult result)
        {
            if (_isStopping) return;
            try
            {
                _clientSocket = _listener.EndAcceptSocket(result);
                string address = ((IPEndPoint)_clientSocket.RemoteEndPoint).Address.ToString();
                if (string.Compare(address, "127.0.0.1", StringComparison.Ordinal) != 0)
                {
                    bool matched = false;
                    //    if (UserConfiguration.SystemOptions.ExternalHosts != null) {
                    //        for (int index = 0; index <= UserConfiguration.SystemOptions.ExternalHosts.Count - 1; index++) {
                    //            if (string.Compare(address, UserConfiguration.SystemOptions.ExternalHosts(index), StringComparison.Ordinal) == 0 || string.Compare(UserConfiguration.SystemOptions.ExternalHosts(index), "0.0.0.0", StringComparison.Ordinal) == 0) {
                    //                matched = true;
                    //                break;
                    //            }
                    //        }
                    //    }
                    if (!matched) return;
                }
                _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("100 MusicBee\n"));
                byte[] buffer = new byte[4096];
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
                                int bytesRead = _clientSocket.Receive(buffer, count, 4096 - count, SocketFlags.None);
                                if (bytesRead == 0)
                                {
                                    connectionClosing = true;
                                    break;
                                }
                                count += bytesRead;
                                eocIndex = Array.LastIndexOf<byte>(buffer, 10, count - 1);
                            }
                        } while ((count < 4096) && (eocIndex == -1));
                    }
                    catch
                    {
                        connectionClosing = true;
                    }
                    string[] commands;
                    if (count == 0)
                    {
                        commands = new string[20];
                    }
                    else
                    {
                        commands =
                            System.Text.Encoding.UTF8.GetString(buffer, 0, count).Replace("\r\n", "\n").Split('\n');
                    }
                    foreach (string commandLine in commands)
                    {
                        try
                        {
                            switch (commandLine)
                            {
                                case "NEXT":
                                    _plugin.PlayNextTrack();
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("250 OK\n"));
                                    break;
                                case "PLAYPAUSE":
                                    _plugin.PlayPauseTrack();
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("250 OK\n"));
                                    break;
                                case "PREVIOUS":
                                    _plugin.PlayPreviousTrack();
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("250 OK\n"));
                                    break;
                                case "INCREASEVOL":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("260 VOL UP:{0}\n", _plugin.IncreaseVolume())));
                                    break;
                                case "DECREASEVOL":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("270 VOL DOWN:{0}\n", _plugin.DecreaseVolume())));
                                    break;
                                    
                                default:
                                    break;


                            }
                        }
                        catch (ThreadAbortException ex)
                        {
                        }
                        catch
                        {
                            try
                            {
                                _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("Error"));
                            }
                            catch
                            {
                            }
                        }
                    }
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
            catch
            {
            }
            finally
            {
                if (_clientSocket != null)
                {
                    try
                    {
                        _clientSocket.Close();
                    }
                    catch
                    {
                    }
                    _clientSocket = null;
                }
                _clientConnected.Set();
            }
        }
    }
}

