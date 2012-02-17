using System;
using System.Globalization;
using System.Threading;
using System.Xml;

namespace MusicBeePlugin
{
    class ProtocolHandler
    {
        private static XmlDocument _xmlDoc = new XmlDocument();
        private static XmlNode _xmlNode;
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
        public const string Scrobble = "scrobble";
        public const string Lyrics = "lyrics";
        public const string Rating = "rating";
        public const string PlayerStatus = "playerStatus";
        public const string Error = "error";

        private static String PrepareXml(String name, String content, bool isFinishedByNullChar)
        {
            string result = "<" + name + ">" + content + "</" + name + ">";
            if (isFinishedByNullChar)
                return result + "\0";
            return result;
        }

        private static string GetPlayerStatus()
        {
            string playerstatus = "";
//             playerstatus += String.Format("<repeat>{0}</repeat>", _plugin.PlayerRepeatState("state"));
//             playerstatus += String.Format("<mute>{0}</mute>", _plugin.PlayerMuteState("state"));
//             playerstatus += String.Format("<shuffle>{0}</shuffle>", _plugin.PlayerShuffleState("state"));
//             playerstatus += String.Format("<scrobbler>{0}</scrobbler>", _plugin.ScrobblerState("state"));
//             playerstatus += String.Format("<playState>{0}</playState>", _plugin.PlayerPlayState());
//             playerstatus += String.Format("<volume>{0}</volume>", _plugin.PlayerVolume("-1"));
            return playerstatus;
        }

        private static string GetSongInfo()
        {
            string songInfo = "";
//             songInfo += String.Format("<artist>{0}</artist>", _plugin.GetCurrentTrackArtist());
//             songInfo += String.Format("<title>{0}</title>", _plugin.GetCurrentTrackTitle());
//             songInfo += String.Format("<album>{0}</album>", _plugin.GetCurrentTrackAlbum());
//             songInfo += String.Format("<year>{0}</year>", _plugin.GetCurrentTrackYear());
            return songInfo;
        }

        public static void ProcessIncomingMessage(string incomingMessage)
        {
            string xmlData = "<serverData>" + incomingMessage.Replace("\0", "") + "</serverData>";
            _xmlDoc.LoadXml(xmlData);
            foreach (XmlNode xmNode in _xmlDoc.FirstChild.ChildNodes)
            {
                try
                {
                    switch (xmNode.Name)
                    {
                        case Next:
                            SocketServer.Send(PrepareXml(Next, _plugin.PlayerPlayNextTrack(), true));
                            break;
                        case Previous:
                            SocketServer.Send(PrepareXml(Previous, _plugin.PlayerPlayPreviousTrack(), true));
                            break;
                        case PlayPause:
                            SocketServer.Send(PrepareXml(PlayPause,  _plugin.PlayerPlayPauseTrack(), true));
                            break;
                        case PlayState:
                            SocketServer.Send(PrepareXml(PlayState, actionContent, true));
                            break;
                        case Volume:
                            SocketServer.Send(PrepareXml(Volume, actionContent, true));
                            break;
                        case SongChangedStatus:
                            SocketServer.Send(PrepareXml(SongChangedStatus, actionContent, true));
                            break;
                        case SongInformation:
                            SocketServer.Send(PrepareXml(SongInformation, actionContent, true));
                            break;
                        case SongCover:
                            new Thread(
                                () =>
                                SocketServer.Send(PrepareXml(SongCover, actionContent, true))).Start();
                            break;
                        case Stop:
                            SocketServer.Send(PrepareXml(Stop, _plugin.Stop(), true));
                            break;
                        case Shuffle:
                            SocketServer.Send(PrepareXml(Shuffle, actionContent, true));
                            break;
                        case Mute:
                            SocketServer.Send(PrepareXml(Mute, actionContent, true));
                            break;
                        case Repeat:
                            SocketServer.Send(PrepareXml(Repeat,actionContent,true));
                            break;
                        case Playlist:
                            SocketServer.Send(PrepareXml(Playlist,actionContent),true);
                            break;
                        case PlayNow:
                            _plugin.PlaylistGoToSpecifiedTrack(xmNode.InnerText);
                            SocketServer.Send( "<playNow/>\0");
                            break;
                        case Scrobble:
                            SocketServer.Send(PrepareXml(Scrobble,actionContent,true));
                            break;
                        case Lyrics:
                            new Thread(
                                () =>
                                SocketServer.Send(PrepareXml(Lyrics,actionContent,true))).
                                Start();
                            break;
                        case Rating:
                            SocketServer.Send(PrepareXml(Rating,actionContent,true));
                            break;
                        case PlayerStatus:
                            SocketServer.Send(PrepareXml(PlayerStatus,actionContent,true));
                            break;
                    }
                    SocketServer.Send("\r\n");
                }
                catch
                {
                    try
                    {
                        SocketServer.Send(PrepareXml(Error,string.Empty,true));
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
