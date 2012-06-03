using System;
using System.Diagnostics;
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

        private string GetPlayerStatus(double clientProtocolVersion, PlayerStatus status)
        {
            if (clientProtocolVersion >= 1)
            {
                string playerstatus = PrepareXml(Constants.Repeat, status.RepeatState, false,
                                                 false);
                playerstatus += PrepareXml(Constants.Mute, status.MuteState, false, false);
                playerstatus += PrepareXml(Constants.Shuffle, status.ShuffleState, false, false);
                playerstatus += PrepareXml(Constants.Scrobble, status.ScrobblerState, false, false);
                //playerstatus += PrepareXml(Constants.PlayState, status.PlayState, false, false);
                playerstatus += PrepareXml(Constants.Volume, status.Volume, false, false);
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
                                OnRequestAvailable(new ClientRequestArgs(RequestType.Volume, clientId, xmNode.InnerText));
                                break;
                            case Constants.SongInformation:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.SongInformation, clientId));
                                break;
                            case Constants.SongCover:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.SongCover, clientId));
                                break;
                            case Constants.Stop:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.Stop, clientId));
                                break;
                            case Constants.Shuffle:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.ShuffleState, clientId, xmNode.InnerText));
                                break;
                            case Constants.Mute:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.MuteState, clientId, xmNode.InnerText));
                                break;
                            case Constants.Repeat:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.RepeatState, clientId, xmNode.InnerText));
                                break;
                            case Constants.Playlist:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.Playlist,clientId));
                                break;
                            case Constants.PlayNow:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.PlayNow, clientId, xmNode.InnerText));
                                break;
                            case Constants.Scrobble:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.ScrobblerState, clientId, xmNode.InnerText));
                                break;
                            case Constants.Lyrics:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.Lyrics,clientId));
                                break;
                            case Constants.Rating:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.Rating, clientId, xmNode.InnerText));
                                break;
                            case Constants.PlayerStatus:
                                OnRequestAvailable(new ClientRequestArgs(RequestType.PlayerStatus,clientId));
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

        private void HandlePlayerStatusReceived(PlayerStatus status, int clientId)
        {
            string message =PrepareXml(Constants.PlayerStatus, GetPlayerStatus(_clientProtocolVersion,status), true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void RatingRequestHandled(string rating, int clientId)
        {
            string message = PrepareXml(Constants.Rating,rating, true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void LyricsRequestHandled(string lyrics, int clientId)
        {
            string message = PrepareXml(Constants.Lyrics, lyrics, true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void PlayNowRequestHandled(string status, int clientId)
        {
            string message = PrepareXml(Constants.PlayNow, status,true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void PlaylistRequestHandled(string playlistData, int clientId)
        {
            string message = PrepareXml(Constants.Playlist, playlistData, true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void StopRequestHandled(string status, int clientId)
        {
                string message = PrepareXml(Constants.Stop, status, true, true);
                OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void SongCoverRequestHandled(string cover, int clientId)
        {
            string message = PrepareXml(Constants.SongCover, cover, true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void SongInfomationRequestHandled(TrackInfo track, int clientId)
        {
            string message = PrepareXml(Constants.SongInformation, GetSongInfo(track, _clientProtocolVersion), true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void PlayStateRequestHandled(string status, int clientId)
        {
            string message = PrepareXml(Constants.PlayState, status, true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void PlayPauseRequestHandled(string status, int clientId)
        {
            string message = PrepareXml(Constants.PlayPause, status, true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }

        public void PreviousTrackRequestHandled(string status, int clientId)
        {
            string message = PrepareXml(Constants.Previous, status, true, true);
            OnReplyAvailable(new MessageEventArgs(message,clientId));
        }


        public void NextTrackRequestHandled(string status, int clientId)
        {
            string message = PrepareXml(Constants.Next, status, true, true);
            OnReplyAvailable(new MessageEventArgs(message, clientId));
        }
    }
}