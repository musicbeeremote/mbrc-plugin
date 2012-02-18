using System;
using System.Globalization;
using System.Threading;
using System.Xml;

namespace MusicBeePlugin
{
    internal class ProtocolHandler
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

        private static readonly ProtocolHandler ProtocolHandlerInstance = new ProtocolHandler();

        private IPlugin _plugin;

        static ProtocolHandler()
        {
        }

        private ProtocolHandler()
        {
            _xmlDoc = new XmlDocument();
        }

        public static ProtocolHandler Instance
        {
            get { return ProtocolHandlerInstance; }
        }

        public void Initialize(IPlugin plugin)
        {
            _plugin = plugin;
        }

        private string PrepareXml(string name, string content, bool isNullFinished)
        {
            string result = "<" + name + ">" + content + "</" + name + ">";
            if (isNullFinished)
                return result + "\0";
            return result;
        }

        private string GetPlayerStatus()
        {
            string playerstatus = PrepareXml(Repeat, _plugin.PlayerRepeatState(State), false);
            playerstatus += PrepareXml(Mute, _plugin.PlayerMuteState(State), false);
            playerstatus += PrepareXml(Shuffle, _plugin.PlayerShuffleState(State), false);
            playerstatus += PrepareXml(Scrobble, _plugin.ScrobblerState(State), false);
            playerstatus += PrepareXml(PlayState, _plugin.PlayerPlayState(), false);
            playerstatus += PrepareXml(Volume, _plugin.PlayerVolume(String.Empty), false);
            return playerstatus;
        }

        private string GetSongInfo()
        {
            string songInfo = PrepareXml(Artist, _plugin.GetCurrentTrackArtist(), false);
            songInfo += PrepareXml(Title, _plugin.GetCurrentTrackArtist(), false);
            songInfo += PrepareXml(Album, _plugin.GetCurrentTrackTitle(), false);
            songInfo += PrepareXml(Year, _plugin.GetCurrentTrackAlbum(), false);
            return songInfo;
        }

        public void ProcessIncomingMessage(string incomingMessage)
        {
            if (String.IsNullOrEmpty(incomingMessage))
                return;

            _xmlDoc.LoadXml(PrepareXml("serverData", incomingMessage.Replace("\0", ""), false));

            foreach (XmlNode xmNode in _xmlDoc.FirstChild.ChildNodes)
            {
                try
                {
                    switch (xmNode.Name)
                    {
                        case Next:
                            SocketServer.Instance.Send(PrepareXml(Next, _plugin.PlayerPlayNextTrack(), true));
                            break;
                        case Previous:
                            SocketServer.Instance.Send(PrepareXml(Previous, _plugin.PlayerPlayPreviousTrack(), true));
                            break;
                        case PlayPause:
                            SocketServer.Instance.Send(PrepareXml(PlayPause, _plugin.PlayerPlayPauseTrack(), true));
                            break;
                        case PlayState:
                            SocketServer.Instance.Send(PrepareXml(PlayState, _plugin.PlayerPlayState(), true));
                            break;
                        case Volume:
                            SocketServer.Instance.Send(PrepareXml(Volume, _plugin.PlayerVolume(xmNode.InnerText), true));
                            break;
                        case SongChangedStatus:
                            SocketServer.Instance.Send(PrepareXml(SongChangedStatus,
                                                                  _plugin.SongChanged.ToString(
                                                                      CultureInfo.InvariantCulture), true));
                            break;
                        case SongInformation:
                            SocketServer.Instance.Send(PrepareXml(SongInformation, GetSongInfo(), true));
                            break;
                        case SongCover:
                            new Thread(
                                () =>
                                SocketServer.Instance.Send(PrepareXml(SongCover, _plugin.GetCurrentTrackCover(), true)))
                                .Start();
                            break;
                        case Stop:
                            SocketServer.Instance.Send(PrepareXml(Stop, _plugin.PlayerStopPlayback(), true));
                            break;
                        case Shuffle:
                            SocketServer.Instance.Send(PrepareXml(Shuffle, _plugin.PlayerShuffleState(xmNode.InnerText),
                                                                  true));
                            break;
                        case Mute:
                            SocketServer.Instance.Send(PrepareXml(Mute, _plugin.PlayerMuteState(xmNode.InnerText), true));
                            break;
                        case Repeat:
                            SocketServer.Instance.Send(PrepareXml(Repeat, _plugin.PlayerRepeatState(xmNode.InnerText),
                                                                  true));
                            break;
                        case Playlist:
                            SocketServer.Instance.Send(PrepareXml(Playlist, _plugin.PlaylistGetTracks(), true));
                            break;
                        case PlayNow:
                            SocketServer.Instance.Send(PrepareXml(PlayNow,
                                                                  _plugin.PlaylistGoToSpecifiedTrack(xmNode.InnerText),
                                                                  true));
                            break;
                        case Scrobble:
                            SocketServer.Instance.Send(PrepareXml(Scrobble, _plugin.ScrobblerState(xmNode.InnerText),
                                                                  true));
                            break;
                        case Lyrics:
                            new Thread(
                                () =>
                                SocketServer.Instance.Send(PrepareXml(Lyrics, _plugin.RetrieveCurrentTrackLyrics(), true)))
                                .
                                Start();
                            break;
                        case Rating:
                            SocketServer.Instance.Send(PrepareXml(Rating, _plugin.TrackRating(xmNode.InnerText), true));
                            break;
                        case PlayerStatus:
                            SocketServer.Instance.Send(PrepareXml(PlayerStatus, GetPlayerStatus(), true));
                            break;
                    }
                    SocketServer.Instance.Send("\r\n");
                }
                catch
                {
                    try
                    {
                        SocketServer.Instance.Send(PrepareXml(Error, xmNode.Name, true));
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