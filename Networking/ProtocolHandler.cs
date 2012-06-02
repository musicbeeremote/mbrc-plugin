using System;
using System.Diagnostics;
using System.Threading;
using System.Xml;
using MusicBeePlugin.Entities;
using MusicBeePlugin.Error;
using MusicBeePlugin.Events;
using MusicBeePlugin.Utilities;

namespace MusicBeePlugin.Networking
{
    internal class ProtocolHandler
    {
        private readonly XmlDocument _xmlDoc;

        private double _clientProtocolVersion = 1.0;

        public event EventHandler<MessageEventArgs> ReplyAvailable;
        public event EventHandler<MessageEventArgs> DisconnectClient;
        public event EventHandler<ClientRequestArgs> RequestAvailable;

        private void OnReplyAvailable(MessageEventArgs args)
        {
            EventHandler<MessageEventArgs> handler = ReplyAvailable;
            if (handler != null) handler(this, args);
        }

        private void OnDisconnectClient(MessageEventArgs args)
        {
            EventHandler<MessageEventArgs> handler = DisconnectClient;
            if (handler != null) handler(this, args);
        }

        private void OnRequestAvailable(ClientRequestArgs args)
        {
            EventHandler<ClientRequestArgs> handler = RequestAvailable;
            if (handler != null) handler(this, args);
        }

        public ProtocolHandler()
        {
            _xmlDoc = new XmlDocument();
        }

        public void ShuffleStateChanged(string shuffleState, int clientId)
        {
            string packet = PrepareXml(Constants.Shuffle, shuffleState, true, true);
            OnReplyAvailable(new MessageEventArgs(packet, clientId));
        }

        public void ScrobbleStateChanged(string scrobbleState, int clientId)
        {
            string packet = PrepareXml(Constants.Scrobble, scrobbleState, true, true);
            OnReplyAvailable(new MessageEventArgs(packet, clientId));
        }

        public void RepeatStateChanged(string repeatState, int clientId)
        {
            string packet = PrepareXml(Constants.Repeat, repeatState, true, true);
            OnReplyAvailable(new MessageEventArgs(packet, clientId));
        }

        public void MuteStateChanged(string muteState, int clientId)
        {
            string mutePacket = PrepareXml(Constants.Mute, muteState, true, true);
            OnReplyAvailable(new MessageEventArgs(mutePacket, clientId));
        }

        public void VolumeLevelChanged(string volumeLevel, int clientId)
        {
            string packet = PrepareXml(Constants.Volume, volumeLevel, true, true);
            OnReplyAvailable(new MessageEventArgs(packet, clientId));
        }

        public void TrackChanged(TrackInfo track, string cover, int clientId)
        {
            string message = PrepareXml(Constants.SongInformation, GetSongInfo(track,_clientProtocolVersion), true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
            string message2 = (PrepareXml(Constants.SongCover, cover, true, true));
            OnReplyAvailable(new MessageEventArgs(message2, clientId));

        }

        public void PlayStateChanged(string playstate, int clientId)
        {
            string packet = PrepareXml(Constants.PlayState, playstate, true, true);
            OnReplyAvailable(new MessageEventArgs(packet, clientId));
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

        private string GetSongInfo(TrackInfo track, double clientProtocolVersion)
        {
            if (clientProtocolVersion >= 1)
            {
                string songInfo = PrepareXml(Constants.Artist, track.Artist, false, false);
                songInfo += PrepareXml(Constants.Title, track.Title, false, false);
                songInfo += PrepareXml(Constants.Album, track.Album, false, false);
                songInfo += PrepareXml(Constants.Year, track.Year, false, false);
                return songInfo;
            }
            return string.Empty;
        }

        /// <summary>
        /// Processes the incoming message and answer's sending back the needed data.
        /// </summary>
        /// <param name="incomingMessage">The incoming message.</param>
        /// <param name="clientId"> </param>
        public void ProcessIncomingMessage(string incomingMessage, int clientId)
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

                foreach (XmlNode xmNode in _xmlDoc.FirstChild.ChildNodes)
                {
                    if (Authenticator.Client(clientId).PacketNumber == 0 && xmNode.Name != Constants.Player)
                    {
                        OnDisconnectClient(new MessageEventArgs(clientId));
                    }
                    else if (Authenticator.Client(clientId).PacketNumber == 1 && xmNode.Name != Constants.Protocol)
                    {
                        OnDisconnectClient(new MessageEventArgs(clientId));
                    }
                    else if (Authenticator.Client(clientId).PacketNumber >= 1)
                    {
                        Authenticator.Client(clientId).Authenticated = true;
                    }
                    try
                    {
                        switch (xmNode.Name)
                        {
                            case Constants.Next:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.PlayNext,clientId));
                                break;
                            case Constants.Previous:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.PlayPrevious,clientId));
                                break;
                            case Constants.PlayPause:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.PlayPause, clientId));
                                break;
                            case Constants.PlayState:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.PlayState, clientId));
                                break;
                            case Constants.Volume:
                                HandleVolumeReceived(clientId, xmNode);
                                break;
                            case Constants.SongInformation:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.SongInformation, clientId));
                                break;
                            case Constants.SongCover:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.SongCover, clientId));
                                HandleSongCoverReceived(clientId);
                                break;
                            case Constants.Stop:
                                HandleStopReceived(clientId);
                                break;
                            case Constants.Shuffle:
                                HandleShuffleReceived(clientId, xmNode);
                                break;
                            case Constants.Mute:
                                HandleMuteReceived(clientId, xmNode);
                                break;
                            case Constants.Repeat:
                                HandleRepeatReceived(clientId, xmNode);
                                break;
                            case Constants.Playlist:
                                HandlePlaylistReceived(clientId);
                                break;
                            case Constants.PlayNow:
                                HandlePlayNowReceived(clientId, xmNode);
                                break;
                            case Constants.Scrobble:
                                HandleScrobbleReceived(clientId, xmNode);
                                break;
                            case Constants.Lyrics:
                                HandleLyricsReceived(clientId);
                                break;
                            case Constants.Rating:
                                HandleRatingReceived(clientId, xmNode);
                                break;
                            case Constants.PlayerStatus:
                                HandlePlayerStatusReceived(clientId);
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
                                OnReplyAvailable(new MessageEventArgs(message, clientId));
                                break;
                            case Constants.Player:
                                string packet = PrepareXml(Constants.Player, Constants.PlayerName, true, true);
                                OnReplyAvailable(new MessageEventArgs(packet, clientId));
                                break;
                        }
                    }
                    catch
                    {
                        try
                        {
                            string packet = PrepareXml(Constants.Error, xmNode.Name, true, true);
                            OnReplyAvailable(new MessageEventArgs(packet, clientId));
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            ErrorHandler.LogError(ex);
#endif
                        }
                    }
                    Authenticator.Client(clientId).IncreasePacketNumber();
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
            //if (clientId == -1)
            //{
            //    SocketServer.Instance.Send(PrepareXml(Constants.SongChangedStatus,
            //                                          _plugin.SongChanged.ToString(
            //                                              CultureInfo.InvariantCulture), true, true));
            //}
            //else
            //{
            //    SocketServer.Instance.Send(PrepareXml(Constants.SongChangedStatus,
            //                                          _plugin.SongChanged.ToString(
            //                                              CultureInfo.InvariantCulture), true, true), clientId);
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
            //if (clientId == -1)
            //{
            //    SocketServer.Instance.Send(PrepareXml(Constants.PlayState, _plugin.PlayerPlayState(), true, true));
            //}
            //else
            //{
            //    SocketServer.Instance.Send(PrepareXml(Constants.PlayState, _plugin.PlayerPlayState(), true, true),
            //                               clientId);
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


        public void NextTrackPlayed(string status, int clientId)
        {
            string message = PrepareXml(Constants.Next, status, true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }
    }
}