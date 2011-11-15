using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Xml;

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
                _listener = new TcpListener(IPAddress.Any, 3000);
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
                //if (string.Compare(address, "127.0.0.1", StringComparison.Ordinal) != 0)
                //{
                   // bool matched = false;
                    //    if (UserConfiguration.SystemOptions.ExternalHosts != null) {
                    //        for (int index = 0; index <= UserConfiguration.SystemOptions.ExternalHosts.Count - 1; index++) {
                    //            if (string.Compare(address, UserConfiguration.SystemOptions.ExternalHosts(index), StringComparison.Ordinal) == 0 || string.Compare(UserConfiguration.SystemOptions.ExternalHosts(index), "0.0.0.0", StringComparison.Ordinal) == 0) {
                    //                matched = true;
                    //                break;
                    //            }
                    //        }
                    //    }
                   // if (!matched) return;
               // }
                _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<MusicBeeClientIP>{0}</MusicBeeClientIP>\0", address)));
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
                  
                    string clientData;
                    if (count == 0)
                    {
                        clientData = "";
                    }
                    else
                    {
                        clientData = System.Text.Encoding.UTF8.GetString(buffer, 0, count).Replace("\r\n","");
                    }
                    string xmlData = "<serverData>" + clientData.Replace("\0","") + "</serverData>";
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlData);
                    foreach (XmlNode xmNode in xmlDoc.FirstChild.ChildNodes)
                    {
                        try
                        {
                            switch (xmNode.Name)
                            {
                                case "next":
                                    _plugin.PlayerPlayNextTrack();
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<next>OK</next>\0"));
                                    break;
                                case "previous":
                                    _plugin.PlayerPlayPreviousTrack();
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<previous>OK</previous>\0"));
                                    break;
                                case "playPause":
                                    _plugin.PlayerPlayPauseTrack();
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<playPause>OK</playPause>\0"));
                                    break;
                                case "playState":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<playState>{0}</playState>\0", _plugin.PlayerPlayState())));
                                    break;
                                case "volume":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<volume>{0}</volume>\0", _plugin.PlayerVolume(xmNode.InnerText))));
                                    break;
                                case "songChanged":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<songChanged>{0}</songChanged>\0", _plugin.SongChanged)));
                                    _plugin.SongChanged = false;
                                    break;
                                case "songInfo":
                                    if (_plugin.CurrentSong == null)
                                    {
                                        _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<nulldata/>\0"));
                                        break;
                                    }     
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<songInfo>"));
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<artist>{0}</artist>", _plugin.CurrentSong.Artist)));
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<title>{0}</title>", _plugin.CurrentSong.Title)));
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<album>{0}</album>", _plugin.CurrentSong.Album)));
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<year>{0}</year>", _plugin.CurrentSong.Year)));
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("</songInfo>\0"));
                                    break;
                                case "songCover":
                                    if (_plugin.CurrentSong ==null)
                                    {
                                        _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<nulldata/>\0"));
                                        break;
                                    }
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<songCover>{0}</songCover>\0", _plugin.CurrentSong.ResizedImage())));
                                    break;
                                case "stopPlayback":
                                    _plugin.PlayerStopPlayback();
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<stopPlayback></stopPlayback>\0"));
                                    break;
                                case "shuffle":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<shuffle>{0}</shuffle>\0", _plugin.PlayerShuffleState(xmNode.InnerText))));
                                    break;
                                case "mute":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<mute>{0}</mute>\0", _plugin.PlayerMuteState(xmNode.InnerText))));
                                    break;
                                case "repeat":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<repeat>{0}</repeat>\0", _plugin.PlayerRepeatState(xmNode.InnerText))));
                                    break;
                                case "playlist":
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(String.Format("<playlist>{0}</playlist>\0", _plugin.PlaylistGetTracks())));
                                    break;
                                case "playNow":
                                    _plugin.PlaylistGoToSpecifiedTrack(xmNode.InnerText);
                                    _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<playNow/>\0"));
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
                                _clientSocket.Send(System.Text.Encoding.UTF8.GetBytes("<error/>\0"));
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

