using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Xml;
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
        private static Plugin _plugin;
        private static XmlDocument _xmlDoc;

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
        /// It gives access to from the socket server to the plugin's internal methods.
        /// </summary>
        /// <param name="plugin">plugin</param>
        /// <returns></returns>
        public void ConnectToPlugin(Plugin plugin)
        {
            _plugin = plugin;
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
                _listenerThread = new Thread(ListenForClients) {IsBackground = true, Priority = ThreadPriority.Lowest};
                _listenerThread.Start();
                _xmlDoc = new XmlDocument();
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
                    _listener.BeginAcceptSocket(AcceptSocketCallback, state: null);
                    _clientConnected.WaitOne();
                }
                catch
                {
                    Debug.WriteLine("Listener exception");
                }
            } while (!_isStopping);
        }

        private static void Send(Socket handler,string data)
        {
            byte[] byteData = System.Text.Encoding.UTF8.GetBytes(data);
            handler.Send(byteData);
        }

        private static void AcceptSocketCallback(IAsyncResult result)
        {
            if (_isStopping) return;
            try
            {
                _clientSocket = _listener.EndAcceptSocket(result);
                string address = ((IPEndPoint) _clientSocket.RemoteEndPoint).Address.ToString();
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
                _clientSocket.Send(
                    System.Text.Encoding.UTF8.GetBytes(String.Format("<MusicBeeClientIP>{0}</MusicBeeClientIP>\0",
                                                                     address)));
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

                    string clientData = count == 0 ? "" : System.Text.Encoding.UTF8.GetString(buffer, 0, count).Replace("\r\n", "");
                    string xmlData = "<serverData>" + clientData.Replace("\0", "") + "</serverData>";
                    _xmlDoc.LoadXml(xmlData);
                    foreach (XmlNode xmNode in _xmlDoc.FirstChild.ChildNodes)
                    {
                        if (_isStopping) return;
                        try
                        {
                            switch (xmNode.Name)
                            {
                                case "next":
                                    _plugin.PlayerPlayNextTrack();
                                    Send(_clientSocket,"<next>OK</next>\0");
                                    break;
                                case "previous":
                                    _plugin.PlayerPlayPreviousTrack();
                                    Send(_clientSocket,"<previous>OK</previous>\0");
                                    break;
                                case "playPause":
                                    _plugin.PlayerPlayPauseTrack();
                                    Send(_clientSocket,"<playPause>OK</playPause>\0");
                                    break;
                                case "playState":
                                    Send(_clientSocket,String.Format(
                                            "<playState>{0}</playState>\0", _plugin.PlayerPlayState()));
                                    break;
                                case "volume":
                                    Send(_clientSocket,String.Format("<volume>{0}</volume>\0",
                                                                                         _plugin.PlayerVolume(
                                                                                             xmNode.InnerText)));
                                    break;
                                case "songChanged":
                                    Send(_clientSocket,String.Format("<songChanged>{0}</songChanged>\0", _plugin.SongChanged));
                                    _plugin.SongChanged = false;
                                    break;
                                case "songInfo":
                                    Send(_clientSocket, "<songInfo>");
                                    Send(_clientSocket, String.Format("<artist>{0}</artist>",
                                                                      _plugin.GetCurrentTrackArtist()));
                                    Send(_clientSocket, String.Format("<title>{0}</title>",
                                                                      _plugin.GetCurrentTrackTitle()));
                                    Send(_clientSocket, String.Format("<album>{0}</album>",
                                                                      _plugin.GetCurrentTrackAlbum()));
                                    Send(_clientSocket, String.Format("<year>{0}</year>",
                                                                      _plugin.GetCurrentTrackYear()));
                                    Send(_clientSocket, "</songInfo>\0");
                                    break;
                                case "songCover":
                                    new Thread(
                                        () =>
                                        Send(_clientSocket,
                                             String.Format("<songCover>{0}</songCover>\0",
                                                           _plugin.GetCurrentTrackCover()))).Start();
                                    break;
                                case "stopPlayback":
                                    _plugin.PlayerStopPlayback();
                                    Send(_clientSocket,"<stopPlayback></stopPlayback>\0");
                                    break;
                                case "shuffle":
                                    Send(_clientSocket,String.Format("<shuffle>{0}</shuffle>\0",
                                                                                         _plugin.PlayerShuffleState(
                                                                                             xmNode.InnerText)));
                                    break;
                                case "mute":
                                    Send(_clientSocket,String.Format("<mute>{0}</mute>\0",
                                                                                         _plugin.PlayerMuteState(
                                                                                             xmNode.InnerText)));
                                    break;
                                case "repeat":
                                    Send(_clientSocket,String.Format("<repeat>{0}</repeat>\0",
                                                                                         _plugin.PlayerRepeatState(
                                                                                             xmNode.InnerText)));
                                    break;
                                case "playlist":
                                        Send(_clientSocket,String.Format("<playlist>{0}</playlist>\0",
                                                                                         _plugin.PlaylistGetTracks()));
                                    break;
                                case "playNow":
                                    _plugin.PlaylistGoToSpecifiedTrack(xmNode.InnerText);
                                    Send(_clientSocket,"<playNow/>\0");
                                    break;
                                case "scrobbler":
                                    Send(_clientSocket,String.Format(
                                            "<scrobbler>{0}</scrobbler>\0", _plugin.ScrobblerState(xmNode.InnerText)));
                                    break;
                                case "lyrics":
                                     new Thread(
                                        () =>
                                            Send(_clientSocket,String.Format("<lyrics>{0}</lyrics>\0",
                                                                                         _plugin.
                                                                                             RetrieveCurrentTrackLyrics()))).Start();
                                    break;
                                case "rating":
                                    Send(_clientSocket,String.Format("<rating>{0}</rating>\0",
                                                                                         _plugin.TrackRating(
                                                                                             xmNode.InnerText)));
                                    break;
                                case "playerStatus":
                                    Send(_clientSocket, "<playerStatus>");
                                    Send(_clientSocket, String.Format("<repeat>{0}</repeat>", _plugin.PlayerRepeatState("state")));
                                    Send(_clientSocket, String.Format("<mute>{0}</mute>", _plugin.PlayerMuteState("state")));
                                    Send(_clientSocket, String.Format("<shuffle>{0}</shuffle>",_plugin.PlayerShuffleState("state")));
                                    Send(_clientSocket, String.Format("<scrobbler>{0}</scrobbler>", _plugin.ScrobblerState("state")));
                                    Send(_clientSocket, String.Format("<playState>{0}</playState>", _plugin.PlayerPlayState()));
                                    Send(_clientSocket, String.Format("<volume>{0}</volume>",_plugin.PlayerVolume("-1")));
                                    Send(_clientSocket, "</playerStatus>\0");
                                    break;
                            }
                            Send(_clientSocket,String.Format("\r\n"));
                        }
                        catch (ThreadAbortException ex)
                        {
                            ErrorHandler.LogError(ex);
                        }
                        catch
                        {
                            try
                            {
                                Send(_clientSocket,"<error/>\0");
                            }
                            catch (Exception ex)
                            {
                                ErrorHandler.LogError(ex);
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