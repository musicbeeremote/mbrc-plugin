using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Xml;
using MusicBeePlugin.Error;
using MusicBeePlugin.Events;

namespace MusicBeePlugin.Networking
{
    internal class ProtocolHandler
    {
        private readonly XmlDocument _xmlDoc;

        private double _clientProtocolVersion = 1.0;

        private readonly List<SocketClient> _socketClients;

        public event EventHandler<MessageEventArgs> ReplyAvailable;

        private void OnReplyAvailable(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = ReplyAvailable;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<MessageEventArgs> DisconnectClient;

        private void OnDisconnectClient(MessageEventArgs e)
        {
            EventHandler<MessageEventArgs> handler = DisconnectClient;
            if (handler != null) handler(this, e);
        }

        public ProtocolHandler()
        {
            _xmlDoc = new XmlDocument();
            _socketClients = new List<SocketClient>();
        }

        public bool IsClientAuthenticated(int clientId)
        {
            return
                (from socketClient in _socketClients
                 where socketClient.ClientId == clientId
                 select socketClient.Authenticated).FirstOrDefault();
        }

        public void HandleClientDisconnected(object sender, MessageEventArgs e)
        {
            foreach (SocketClient client in _socketClients)
            {
                if (client.ClientId != e.ClientId) continue;
                _socketClients.Remove(client);
                break;
            }
        }

        public void HandleClientConnected(object sender, MessageEventArgs e)
        {
            foreach (SocketClient client in _socketClients)
            {
                if (client.ClientId != e.ClientId) continue;
                _socketClients.Remove(client);
                break;
            }

            SocketClient newClient = new SocketClient(e.ClientId);
            _socketClients.Add(newClient);
        }

        private void HandleShuffleStateChanged(object sender, EventArgs e)
        {
            string packet = PrepareXml(Constants.Shuffle, _plugin.PlayerShuffleState(Constants.State), true, true);
            OnReplyAvailable(new MessageEventArgs(packet));
        }

        private void HandleScrobbleStateChanged(object sender, EventArgs e)
        {
            string packet = PrepareXml(Constants.Scrobble, _plugin.ScrobblerState(Constants.State), true, true);
            OnReplyAvailable(new MessageEventArgs(packet));
        }

        private void HandleRepeatStateChanged(object sender, EventArgs e)
        {
            string packet = PrepareXml(Constants.Repeat, _plugin.PlayerRepeatState(Constants.State), true, true);
            OnReplyAvailable(new MessageEventArgs(packet));
        }

        private void HandleVolumeMuteChanged(object sender, EventArgs e)
        {
            string volumePacket = PrepareXml(Constants.Volume, _plugin.PlayerVolume("get"), true, true);
            string mutePacket = PrepareXml(Constants.Mute, _plugin.PlayerMuteState(Constants.State), true, true);
            OnReplyAvailable(new MessageEventArgs(volumePacket));
            OnReplyAvailable(new MessageEventArgs(mutePacket));
        }

        private void HandleVolumeLevelChanged(object sender, EventArgs e)
        {
            string packet = PrepareXml(Constants.Volume, _plugin.PlayerVolume("get"), true, true);
            OnReplyAvailable(new MessageEventArgs(packet));
        }

        private void HandleTrackChanged(object sender, EventArgs e)
        {
            SocketServer.Instance.Send(PrepareXml(Constants.SongInformation, GetSongInfo(_clientProtocolVersion), true,
                                                  true));
            new Thread(
                () =>
                SocketServer.Instance.Send(PrepareXml(Constants.SongCover, _plugin.CurrentTrackCover, true, true)))
                .Start();
        }

        private void HandlePlayStateChanged(object sender, EventArgs e)
        {
            //string packet = PrepareXml(Constants.PlayState, _plugin.PlayerPlayState(), true, true);
            //ServerMessenger.Instance.OnReplyAvailable(new MessageEventArgs(packet));
        }


        private static string PrepareXml(string name, string content, bool isNullFinished, bool isNewLineFinished)
        {
            string result = "<" + name + ">" + content + "</" + name + ">";
            if (isNullFinished)
                result += "\0";
            if (isNewLineFinished)
                result += "\r\n";
            return result;
        }

        private string GetPlayerStatus(double clientProtocolVersion)
        {
            if (clientProtocolVersion >= 1)
            {
                string playerstatus = PrepareXml(Constants.Repeat, _plugin.PlayerRepeatState(Constants.State), false,
                                                 false);
                playerstatus += PrepareXml(Constants.Mute, _plugin.PlayerMuteState(Constants.State), false, false);
                playerstatus += PrepareXml(Constants.Shuffle, _plugin.PlayerShuffleState(Constants.State), false, false);
                playerstatus += PrepareXml(Constants.Scrobble, _plugin.ScrobblerState(Constants.State), false, false);
                //playerstatus += PrepareXml(Constants.PlayState, _plugin.PlayerPlayState(), false, false);
                playerstatus += PrepareXml(Constants.Volume, _plugin.PlayerVolume(String.Empty), false, false);
                return playerstatus;
            }
            return String.Empty;
        }

        private string GetSongInfo(double clientProtocolVersion)
        {
            if (clientProtocolVersion >= 1)
            {
                string songInfo = PrepareXml(Constants.Artist, _plugin.CurrentTrackArtist, false, false);
                songInfo += PrepareXml(Constants.Title, _plugin.CurrentTrackTitle, false, false);
                songInfo += PrepareXml(Constants.Album, _plugin.CurrentTrackAlbum, false, false);
                songInfo += PrepareXml(Constants.Year, _plugin.CurrentTrackYear, false, false);
                return songInfo;
            }
            return string.Empty;
        }

        /// <summary>
        /// Processes the incoming message and answer's sending back the needed data.
        /// </summary>
        /// <param name="incomingMessage">The incoming message.</param>
        /// <param name="cliendId"> </param>
        public void ProcessIncomingMessage(string incomingMessage, int cliendId)
        {
            try
            {
                if (String.IsNullOrEmpty(incomingMessage))
                    return;
                try
                {
                    Debug.WriteLine(incomingMessage);
                    _xmlDoc.LoadXml(PrepareXml("serverData", incomingMessage.Replace("\0", ""), false, false));
                }
                catch (Exception ex)
                {
#if DEBUG
                    ErrorHandler.LogError(ex);
#endif
                    Debug.WriteLine("Error at: " + incomingMessage);
                }

                int clientIndex = 0;

                foreach (
                    SocketClient socketClient in _socketClients.Where(socketClient => socketClient.ClientId == cliendId)
                    )
                {
                    clientIndex = _socketClients.IndexOf(socketClient);
                }

                foreach (XmlNode xmNode in _xmlDoc.FirstChild.ChildNodes)
                {
                    if (_socketClients[clientIndex].PacketNumber == 0 && xmNode.Name != Constants.Player)
                    {
                        OnDisconnectClient(new MessageEventArgs(cliendId));
                    }
                    else if (_socketClients[clientIndex].PacketNumber == 1 && xmNode.Name != Constants.Protocol)
                    {
                        OnDisconnectClient(new MessageEventArgs(cliendId));
                    }
                    else if (_socketClients[clientIndex].PacketNumber >= 1)
                    {
                        _socketClients[clientIndex].Authenticated = true;
                    }
                    try
                    {
                        switch (xmNode.Name)
                        {
                            case Constants.Next:
                                HandleNextReceived(cliendId);
                                break;
                            case Constants.Previous:
                                HandlePreviousReceived(cliendId);
                                break;
                            case Constants.PlayPause:
                                HandlePlayPauseReceived(cliendId);
                                break;
                            case Constants.PlayState:
                                HandlePlayStateReceived(cliendId);
                                break;
                            case Constants.Volume:
                                HandleVolumeReceived(cliendId, xmNode);
                                break;
                            case Constants.SongChangedStatus:
                                HandleSongChangedStatusReceived(cliendId);
                                break;
                            case Constants.SongInformation:
                                HandleSongInformationReceived(cliendId);
                                break;
                            case Constants.SongCover:
                                HandleSongCoverReceived(cliendId);
                                break;
                            case Constants.Stop:
                                HandleStopReceived(cliendId);
                                break;
                            case Constants.Shuffle:
                                HandleShuffleReceived(cliendId, xmNode);
                                break;
                            case Constants.Mute:
                                HandleMuteReceived(cliendId, xmNode);
                                break;
                            case Constants.Repeat:
                                HandleRepeatReceived(cliendId, xmNode);
                                break;
                            case Constants.Playlist:
                                HandlePlaylistReceived(cliendId);
                                break;
                            case Constants.PlayNow:
                                HandlePlayNowReceived(cliendId, xmNode);
                                break;
                            case Constants.Scrobble:
                                HandleScrobbleReceived(cliendId, xmNode);
                                break;
                            case Constants.Lyrics:
                                HandleLyricsReceived(cliendId);
                                break;
                            case Constants.Rating:
                                HandleRatingReceived(cliendId, xmNode);
                                break;
                            case Constants.PlayerStatus:
                                HandlePlayerStatusReceived(cliendId);
                                break;
                            case Constants.Protocol:
                                string protocolString = xmNode.InnerText;
                                if (!string.IsNullOrEmpty(protocolString))
                                {
                                    if (!Double.TryParse(protocolString, out _clientProtocolVersion))
                                    {
                                        _clientProtocolVersion = 1.0;
                                    }
                                }

                                string message = PrepareXml(Constants.Protocol, Constants.ProtocolVersion, true, true);
                                OnReplyAvailable(new MessageEventArgs(message, cliendId));
                                break;
                            case Constants.Player:
                                string packet = PrepareXml(Constants.Player, Constants.PlayerName, true, true);
                                OnReplyAvailable(new MessageEventArgs(packet, cliendId));
                                break;
                        }
                    }
                    catch
                    {
                        try
                        {
                            string packet = PrepareXml(Constants.Error, xmNode.Name, true, true);
                            OnReplyAvailable(new MessageEventArgs(packet, cliendId));
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            ErrorHandler.LogError(ex);
#endif
                        }
                    }
                    _socketClients[clientIndex].IncreasePacketNumber();
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
            }
        }

        private void HandlePlayerStatusReceived(int cliendId)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.PlayerStatus, GetPlayerStatus(_clientProtocolVersion),
                                                      true, true));
            }
            else
            {
                SocketServer.Instance.Send(
                    PrepareXml(Constants.PlayerStatus, GetPlayerStatus(_clientProtocolVersion), true, true), cliendId);
            }
        }

        private void HandleRatingReceived(int cliendId, XmlNode xmNode)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Rating, _plugin.TrackRating(xmNode.InnerText),
                                                      true,
                                                      true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Rating, _plugin.TrackRating(xmNode.InnerText),
                                                      true,
                                                      true), cliendId);
            }
        }

        private void HandleLyricsReceived(int cliendId)
        {
            Debug.WriteLine(_plugin.CurrentTrackLyrics);
            if (cliendId == -1)
            {
                new Thread(
                    () =>
                    SocketServer.Instance.Send(PrepareXml(Constants.Lyrics,
                                                          _plugin.CurrentTrackLyrics,
                                                          true,
                                                          true)))
                    .
                    Start();
            }
            else
            {
                new Thread(
                    () =>
                    SocketServer.Instance.Send(PrepareXml(Constants.Lyrics,
                                                          _plugin.CurrentTrackLyrics,
                                                          true,
                                                          true), cliendId))
                    .
                    Start();
            }
        }

        private void HandleScrobbleReceived(int cliendId, XmlNode xmNode)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Scrobble, _plugin.ScrobblerState(xmNode.InnerText),
                                                      true, true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Scrobble, _plugin.ScrobblerState(xmNode.InnerText),
                                                      true, true), cliendId);
            }
        }

        private void HandlePlayNowReceived(int cliendId, XmlNode xmNode)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.PlayNow,
                                                      _plugin.PlaylistGoToSpecifiedTrack(
                                                          xmNode.InnerText),
                                                      true, true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.PlayNow,
                                                      _plugin.PlaylistGoToSpecifiedTrack(
                                                          xmNode.InnerText),
                                                      true, true), cliendId);
            }
        }

        private void HandlePlaylistReceived(int cliendId)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Playlist,
                                                      _plugin.PlaylistGetTracks(_clientProtocolVersion),
                                                      true, true));
            }
            else
            {
                SocketServer.Instance.Send(
                    PrepareXml(Constants.Playlist, _plugin.PlaylistGetTracks(_clientProtocolVersion), true, true),
                    cliendId);
            }
        }

        private void HandleRepeatReceived(int cliendId, XmlNode xmNode)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Repeat,
                                                      _plugin.PlayerRepeatState(xmNode.InnerText),
                                                      true, true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Repeat,
                                                      _plugin.PlayerRepeatState(xmNode.InnerText),
                                                      true, true), cliendId);
            }
        }

        private void HandleMuteReceived(int cliendId, XmlNode xmNode)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Mute, _plugin.PlayerMuteState(xmNode.InnerText),
                                                      true,
                                                      true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Mute, _plugin.PlayerMuteState(xmNode.InnerText),
                                                      true,
                                                      true), cliendId);
            }
        }

        private void HandleShuffleReceived(int cliendId, XmlNode xmNode)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Shuffle,
                                                      _plugin.PlayerShuffleState(xmNode.InnerText),
                                                      true, true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Shuffle,
                                                      _plugin.PlayerShuffleState(xmNode.InnerText),
                                                      true, true), cliendId);
            }
        }

        private void HandleStopReceived(int cliendId)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Stop, _plugin.PlayerStopPlayback(), true, true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Stop, _plugin.PlayerStopPlayback(), true, true),
                                           cliendId);
            }
        }

        private void HandleSongCoverReceived(int cliendId)
        {
            if (cliendId == -1)
            {
                new Thread(
                    () =>
                    SocketServer.Instance.Send(PrepareXml(Constants.SongCover, _plugin.CurrentTrackCover,
                                                          true,
                                                          true)))
                    .Start();
            }
            else
            {
                new Thread(
                    () =>
                    SocketServer.Instance.Send(PrepareXml(Constants.SongCover, _plugin.CurrentTrackCover,
                                                          true,
                                                          true), cliendId))
                    .Start();
            }
        }

        private void HandleSongInformationReceived(int cliendId)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.SongInformation, GetSongInfo(_clientProtocolVersion),
                                                      true, true));
            }
            else
            {
                SocketServer.Instance.Send(
                    PrepareXml(Constants.SongInformation, GetSongInfo(_clientProtocolVersion), true, true), cliendId);
            }
        }

        private void HandleSongChangedStatusReceived(int cliendId)
        {
            //if (cliendId == -1)
            //{
            //    SocketServer.Instance.Send(PrepareXml(Constants.SongChangedStatus,
            //                                          _plugin.SongChanged.ToString(
            //                                              CultureInfo.InvariantCulture), true, true));
            //}
            //else
            //{
            //    SocketServer.Instance.Send(PrepareXml(Constants.SongChangedStatus,
            //                                          _plugin.SongChanged.ToString(
            //                                              CultureInfo.InvariantCulture), true, true), cliendId);
            //}
        }

        private void HandleVolumeReceived(int cliendId, XmlNode xmNode)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Volume, _plugin.PlayerVolume(xmNode.InnerText),
                                                      true,
                                                      true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Volume, _plugin.PlayerVolume(xmNode.InnerText),
                                                      true,
                                                      true), cliendId);
            }
        }

        private void HandlePlayStateReceived(int cliendId)
        {
            //if (cliendId == -1)
            //{
            //    SocketServer.Instance.Send(PrepareXml(Constants.PlayState, _plugin.PlayerPlayState(), true, true));
            //}
            //else
            //{
            //    SocketServer.Instance.Send(PrepareXml(Constants.PlayState, _plugin.PlayerPlayState(), true, true),
            //                               cliendId);
            //}
        }

        private void HandlePlayPauseReceived(int cliendId)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.PlayPause, _plugin.PlayerPlayPauseTrack(),
                                                      true,
                                                      true));
            }
            else
            {
                SocketServer.Instance.Send(PrepareXml(Constants.PlayPause, _plugin.PlayerPlayPauseTrack(),
                                                      true,
                                                      true), cliendId);
            }
        }

        private void HandlePreviousReceived(int cliendId)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Previous, _plugin.PlayerPlayPreviousTrack(),
                                                      true,
                                                      true));
            }
            else
            {
                SocketServer.Instance.Send(
                    PrepareXml(Constants.Previous, _plugin.PlayerPlayPreviousTrack(), true,
                               true), cliendId);
            }
        }

        private void HandleNextReceived(int cliendId)
        {
            if (cliendId == -1)
            {
                SocketServer.Instance.Send(PrepareXml(Constants.Next, _plugin.PlayerPlayNextTrack(), true,
                                                      true));
            }
            else
            {
                SocketServer.Instance.Send(
                    PrepareXml(Constants.Next, _plugin.PlayerPlayNextTrack(), true, true), cliendId);
            }
        }
    }
}