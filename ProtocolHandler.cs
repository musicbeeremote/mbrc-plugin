using System;
using System.Globalization;
using System.Threading;
using System.Xml;

namespace MusicBeePlugin
{
    public enum PlayerAction
    {
        PlayPause,
        Previous,
        Next,
        Stop,
        PlayState,
        Volume,
        SongChangedStatus,
        SongInformation,
        SongCover,
        Shuffle,
        Mute,
        Repeat,
        Playlist,
        PlayNow,
        Scrobble,
        Lyrics,
        Rating,
        PlayerStatus
    }

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

        public static String GetActionString(PlayerAction action, String actionContent)
        {
            return InternalGetAction(action, actionContent);
        }

        public static String GetActionString(PlayerAction action)
        {
            return InternalGetAction(action, "");
        }

        private static String PrepareXml(String name, String content, bool isFinishedByNullChar)
        {
            string result = "<" + name + ">" + content + "</" + name + ">";
            if (isFinishedByNullChar)
                return result + "\0";
            return result;
        }

        private static String InternalGetAction(PlayerAction action, String actionContent)
        {
            switch (action)
            {
                case PlayerAction.PlayPause:
                    return PrepareXml(PlayPause, actionContent, true);
                case PlayerAction.Previous:
                    return PrepareXml(Previous, actionContent, true);
                case PlayerAction.Next:
                    return PrepareXml(Next, actionContent, true);
                case PlayerAction.Stop:
                    return PrepareXml(Stop, actionContent, true);
                case PlayerAction.PlayState:
                    return PrepareXml(PlayState, actionContent, true);
                case PlayerAction.Volume:
                    return PrepareXml(Volume, actionContent, true);
                case PlayerAction.SongChangedStatus:
                    return String.Format("<songChanged>{0}</songChanged>\0", actionContent);
                case PlayerAction.SongInformation:
                    return String.Format("<songInfo>{0}</songInfo>\0",actionContent);
                case PlayerAction.SongCover:
                    break;
                case PlayerAction.Shuffle:
                    break;
                case PlayerAction.Mute:
                    break;
                case PlayerAction.Repeat:
                    break;
                case PlayerAction.Playlist:
                    break;
                case PlayerAction.PlayNow:
                    break;
                case PlayerAction.Scrobble:
                    break;
                case PlayerAction.Lyrics:
                    break;
                case PlayerAction.Rating:
                    break;
                case PlayerAction.PlayerStatus:
                    return String.Format("<playerStatus>{0}</playerStatus>\0", actionContent);
                default:
                    throw new ArgumentOutOfRangeException("action");
            }
        }
        private static string GetPlayerStatus()
        {
            string playerstatus = "";
            playerstatus += String.Format("<repeat>{0}</repeat>", _plugin.PlayerRepeatState("state"));
            playerstatus += String.Format("<mute>{0}</mute>", _plugin.PlayerMuteState("state"));
            playerstatus += String.Format("<shuffle>{0}</shuffle>", _plugin.PlayerShuffleState("state"));
            playerstatus += String.Format("<scrobbler>{0}</scrobbler>", _plugin.ScrobblerState("state"));
            playerstatus += String.Format("<playState>{0}</playState>", _plugin.PlayerPlayState());
            playerstatus += String.Format("<volume>{0}</volume>", _plugin.PlayerVolume("-1"));
            return playerstatus;
        }

        private static string GetSongInfo()
        {
            string songInfo = "";
            songInfo += String.Format("<artist>{0}</artist>", _plugin.GetCurrentTrackArtist());
            songInfo += String.Format("<title>{0}</title>", _plugin.GetCurrentTrackTitle());
            songInfo += String.Format("<album>{0}</album>", _plugin.GetCurrentTrackAlbum());
            songInfo += String.Format("<year>{0}</year>", _plugin.GetCurrentTrackYear());
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
                            SocketServer.Send(GetActionString(PlayerAction.Next,
                                                                 _plugin.PlayerPlayNextTrack()));
                            break;
                        case Previous:
                            SocketServer.Send(
                                 GetActionString(PlayerAction.Previous,
                                                                 _plugin.PlayerPlayPreviousTrack()));
                            break;
                        case PlayPause:
                            SocketServer.Send(
                                 GetActionString(PlayerAction.PlayState,
                                                                 _plugin.PlayerPlayPauseTrack()));
                            break;
                        case PlayState:
                            SocketServer.Send(
                                 GetActionString(PlayerAction.PlayState, _plugin.PlayerPlayState()));
                            break;
                        case Volume:
                            SocketServer.Send(
                                 GetActionString(PlayerAction.Volume,
                                                                 _plugin.PlayerVolume(xmNode.InnerText)));
                            break;
                        case SongChangedStatus:
                            SocketServer.Send(
                                 GetActionString(PlayerAction.SongChangedStatus,
                                                                 _plugin.SongChanged.ToString(CultureInfo.InvariantCulture)));
                            _plugin.SongChanged = false;
                            break;
                        case SongInformation:
                            SocketServer.Send(
                                 GetActionString(PlayerAction.SongInformation, SocketServer.GetSongInfo()));
                            break;
                        case SongCover:
                            new Thread(
                                () =>
                                SocketServer.Send(
                                     String.Format("<songCover>{0}</songCover>\0", _plugin.GetCurrentTrackCover()))).
                                Start();
                            break;
                        case Stop:
                            _plugin.PlayerStopPlayback();
                            SocketServer.Send( "<stopPlayback>{0}</stopPlayback>\0");
                            break;
                        case Shuffle:
                            SocketServer.Send(
                                 String.Format("<shuffle>{0}</shuffle>\0", _plugin.PlayerShuffleState(xmNode.InnerText)));
                            break;
                        case Mute:
                            SocketServer.Send(
                                 String.Format("<mute>{0}</mute>\0", _plugin.PlayerMuteState(xmNode.InnerText)));
                            break;
                        case Repeat:
                            SocketServer.Send(
                                 String.Format("<repeat>{0}</repeat>\0", _plugin.PlayerRepeatState(xmNode.InnerText)));
                            break;
                        case Playlist:
                            SocketServer.Send( String.Format("<playlist>{0}</playlist>\0", _plugin.PlaylistGetTracks()));
                            break;
                        case PlayNow:
                            _plugin.PlaylistGoToSpecifiedTrack(xmNode.InnerText);
                            SocketServer.Send( "<playNow/>\0");
                            break;
                        case Scrobble:
                            SocketServer.Send(
                                 String.Format("<scrobbler>{0}</scrobbler>\0", _plugin.ScrobblerState(xmNode.InnerText)));
                            break;
                        case Lyrics:
                            new Thread(
                                () =>
                                SocketServer.Send(String.Format("<lyrics>{0}</lyrics>\0", _plugin.RetrieveCurrentTrackLyrics()))).
                                Start();
                            break;
                        case Rating:
                            SocketServer.Send(String.Format("<rating>{0}</rating>\0", _plugin.TrackRating(xmNode.InnerText)));
                            break;
                        case PlayerStatus:
                            SocketServer.Send(GetActionString(PlayerAction.PlayerStatus, GetPlayerStatus()));
                            break;
                    }
                    SocketServer.Send(String.Format("\r\n"));
                }
                catch
                {
                    try
                    {
                        SocketServer.Send("<error/>\0");
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
