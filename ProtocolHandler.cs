using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Xml;

namespace MusicBeePlugin
{
    internal interface IProtocolHandler
    {
        void ProcessIncomingMessage(string incomingMessage);
    }

    internal class ProtocolHandler : IProtocolHandler
    {
        private readonly XmlDocument _xmlDoc;
        public const string PlayPause = "playPause";
        public const string Previous = "previous";
        public const string Next = "next";
        public const string Stop = "stopPlayback";
        public const string PlayState = "playState";
        public const string Volume = "volume";
        public const string SongChangedStatus = "songChanged";
        public const string SongInformation = "songInfo";
        public const string SongCover = "songCover";
        public const string Shuffle = "shuffle";
        public const string Mute = "mute";
        public const string Repeat = "repeat";
        public const string Playlist = "playlist";
        public const string PlayNow = "playNow";
        public const string Scrobble = "scrobbler";
        public const string Lyrics = "lyrics";
        public const string Rating = "rating";
        public const string PlayerStatus = "playerStatus";
        public const string Error = "error";
        public const string Artist = "artist";
        public const string Title = "title";
        public const string Album = "album";
        public const string Year = "year";
        public const string State = "state";
        public const string PollerState = "pollerState";
        public const string Protocol = "protocol";
        public const string Player = "player";
        public const string ProtocolVersion = "1.0";
        public const string PlayerName = "MusicBee";

        private static readonly ProtocolHandler ProtocolHandlerInstance = new ProtocolHandler();

        private IPlugin _plugin;

        static ProtocolHandler()
        {
        }

        private ProtocolHandler()
        {
            _xmlDoc = new XmlDocument();
            Messenger.Instance.PlayStateChanged += HandlePlayStateChanged;
            Messenger.Instance.TrackChanged += HandleTrackChanged;
            Messenger.Instance.VolumeLevelChanged += HandleVolumeLevelChanged;
            Messenger.Instance.VolumeMuteChanged += HandleVolumeMuteChanged;
            Messenger.Instance.RepeatStateChanged += HandleRepeatStateChanged;
            Messenger.Instance.ScrobbleStateChanged += HandleScrobbleStateChanged;
            Messenger.Instance.ShuffleStateChanged += HandleShuffleStateChanged;
        }

        private void HandleShuffleStateChanged(object sender, EventArgs e)
        {
            SocketServer.Instance.Send(PrepareXml(Shuffle, _plugin.PlayerShuffleState(State),
                                                  true, true));
        }

        private void HandleScrobbleStateChanged(object sender, EventArgs e)
        {
            SocketServer.Instance.Send(PrepareXml(Scrobble, _plugin.ScrobblerState(State),
                                                  true, true));
        }

        private void HandleRepeatStateChanged(object sender, EventArgs e)
        {
            SocketServer.Instance.Send(PrepareXml(Repeat, _plugin.PlayerRepeatState(State), true, true));
        }

        private void HandleVolumeMuteChanged(object sender, EventArgs e)
        {
            SocketServer.Instance.Send(PrepareXml(Volume, _plugin.PlayerVolume("get"), true, true));
            SocketServer.Instance.Send(PrepareXml(Mute, _plugin.PlayerMuteState(State), true, true));
        }

        private void HandleVolumeLevelChanged(object sender, EventArgs e)
        {
            SocketServer.Instance.Send(PrepareXml(Volume, _plugin.PlayerVolume("get"), true, true));
        }

        private void HandleTrackChanged(object sender, EventArgs e)
        {
            SocketServer.Instance.Send(PrepareXml(SongInformation, GetSongInfo(), true, true));
            new Thread(
                () =>
                SocketServer.Instance.Send(PrepareXml(SongCover, _plugin.GetCurrentTrackCover(), true, true)))
                .Start();
        }

        private void HandlePlayStateChanged(object sender, EventArgs e)
        {
            SocketServer.Instance.Send(PrepareXml(PlayState, _plugin.PlayerPlayState(), true, true));
        }

        public static ProtocolHandler Instance
        {
            get { return ProtocolHandlerInstance; }
        }

        public void Initialize(IPlugin plugin)
        {
            _plugin = plugin;
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

        private string GetPlayerStatus()
        {
            string playerstatus = PrepareXml(Repeat, _plugin.PlayerRepeatState(State), false, false);
            playerstatus += PrepareXml(Mute, _plugin.PlayerMuteState(State), false, false);
            playerstatus += PrepareXml(Shuffle, _plugin.PlayerShuffleState(State), false, false);
            playerstatus += PrepareXml(Scrobble, _plugin.ScrobblerState(State), false, false);
            playerstatus += PrepareXml(PlayState, _plugin.PlayerPlayState(), false, false);
            playerstatus += PrepareXml(Volume, _plugin.PlayerVolume(String.Empty), false, false);
            return playerstatus;
        }

        private string GetSongInfo()
        {
            string songInfo = PrepareXml(Artist, _plugin.GetCurrentTrackArtist(), false, false);
            songInfo += PrepareXml(Title, _plugin.GetCurrentTrackTitle(), false, false);
            songInfo += PrepareXml(Album, _plugin.GetCurrentTrackAlbum(), false, false);
            songInfo += PrepareXml(Year, _plugin.GetCurrentTrackYear(), false, false);
            return songInfo;
        }

        /// <summary>
        /// Processes the incoming message and answer's sending back the needed data.
        /// </summary>
        /// <param name="incomingMessage">The incoming message.</param>
        public void ProcessIncomingMessage(string incomingMessage)
        {
            if (String.IsNullOrEmpty(incomingMessage))
                return;
            try
            {
	            _xmlDoc.LoadXml(PrepareXml("serverData", incomingMessage.Replace("\0", ""), false, false));
            }
            catch (Exception ex)
            {
            	ErrorHandler.LogError(ex);
                Debug.WriteLine("Error at: " + incomingMessage);
            }

            foreach (XmlNode xmNode in _xmlDoc.FirstChild.ChildNodes)
            {
                try
                {
                    switch (xmNode.Name)
                    {
                        case Next:
                            SocketServer.Instance.Send(PrepareXml(Next, _plugin.PlayerPlayNextTrack(), true, true));
                            break;
                        case Previous:
                            SocketServer.Instance.Send(PrepareXml(Previous, _plugin.PlayerPlayPreviousTrack(), true,
                                                                  true));
                            break;
                        case PlayPause:
                            SocketServer.Instance.Send(PrepareXml(PlayPause, _plugin.PlayerPlayPauseTrack(), true, true));
                            break;
                        case PlayState:
                            SocketServer.Instance.Send(PrepareXml(PlayState, _plugin.PlayerPlayState(), true, true));
                            break;
                        case Volume:
                            SocketServer.Instance.Send(PrepareXml(Volume, _plugin.PlayerVolume(xmNode.InnerText), true,
                                                                  true));
                            break;
                        case SongChangedStatus:
                            SocketServer.Instance.Send(PrepareXml(SongChangedStatus,
                                                                  _plugin.SongChanged.ToString(
                                                                      CultureInfo.InvariantCulture), true, true));
                            break;
                        case SongInformation:
                            SocketServer.Instance.Send(PrepareXml(SongInformation, GetSongInfo(), true, true));
                            break;
                        case SongCover:
                            new Thread(
                                () =>
                                SocketServer.Instance.Send(PrepareXml(SongCover, _plugin.GetCurrentTrackCover(), true,
                                                                      true)))
                                .Start();
                            break;
                        case Stop:
                            SocketServer.Instance.Send(PrepareXml(Stop, _plugin.PlayerStopPlayback(), true, true));
                            break;
                        case Shuffle:
                            SocketServer.Instance.Send(PrepareXml(Shuffle, _plugin.PlayerShuffleState(xmNode.InnerText),
                                                                  true, true));
                            break;
                        case Mute:
                            SocketServer.Instance.Send(PrepareXml(Mute, _plugin.PlayerMuteState(xmNode.InnerText), true,
                                                                  true));
                            break;
                        case Repeat:
                            SocketServer.Instance.Send(PrepareXml(Repeat, _plugin.PlayerRepeatState(xmNode.InnerText),
                                                                  true, true));
                            break;
                        case Playlist:
                            SocketServer.Instance.Send(PrepareXml(Playlist, _plugin.PlaylistGetTracks(), true, true));
                            break;
                        case PlayNow:
                            SocketServer.Instance.Send(PrepareXml(PlayNow,
                                                                  _plugin.PlaylistGoToSpecifiedTrack(xmNode.InnerText),
                                                                  true, true));
                            break;
                        case Scrobble:
                            SocketServer.Instance.Send(PrepareXml(Scrobble, _plugin.ScrobblerState(xmNode.InnerText),
                                                                  true, true));
                            break;
                        case Lyrics:
                            new Thread(
                                () =>
                                SocketServer.Instance.Send(PrepareXml(Lyrics, _plugin.RetrieveCurrentTrackLyrics(), true,
                                                                      true)))
                                .
                                Start();
                            break;
                        case Rating:
                            SocketServer.Instance.Send(PrepareXml(Rating, _plugin.TrackRating(xmNode.InnerText), true,
                                                                  true));
                            break;
                        case PlayerStatus:
                            SocketServer.Instance.Send(PrepareXml(PlayerStatus, GetPlayerStatus(), true, true));
                            break;
                        case Protocol:
                            SocketServer.Instance.Send(PrepareXml(Protocol, ProtocolVersion, true, true));
                            break;
                        case Player:
                            SocketServer.Instance.Send(PrepareXml(Player, PlayerName, true, true));
                            break;
                    }
                }
                catch
                {
                    try
                    {
                        SocketServer.Instance.Send(PrepareXml(Error, xmNode.Name, true, true));
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.LogError(ex);
                    }
                }
            }
        }
    }
}