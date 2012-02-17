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
        public static string PlayPause = "playPause";
        public static string Previous = "previous";
        public static string Next = "next";
        public static string Stop = "";
        public static string PlayState = "playState";
        public static string Volume = "volume";
        public static string SongChangedStatus = "songChanged";
        public static string SongInformation = "songInfo";
        public static string SongCover = "songCover";
        public static string Shuffle = "";
        public static string Mute = "";
        public static string Repeat = "";
        public static string Playlist = "";
        public static string PlayNow = "";
        public static string Scrobble = "";
        public static string Lyrics = "";
        public static string Rating = "";
        public static string PlayerStatus = "";

        public static String GetActionString(PlayerAction action, String actionContent)
        {
            return InternalGetAction(action, actionContent);
        }

        public static String GetActionString(PlayerAction action)
        {
            return InternalGetAction(action, "");
        }

        private static String InternalGetAction(PlayerAction action, String actionContent)
        {
            const string result = "";
            switch (action)
            {
                case PlayerAction.PlayPause:
                    return String.Format("<playPause>{0}</playPause>\0", actionContent);     
                case PlayerAction.Previous:
                    return String.Format("<previous>{0}</previous>\0", actionContent);
                case PlayerAction.Next:
                    return String.Format("<next>{0}</next>\0", actionContent);
                case PlayerAction.Stop:
                    break;
                case PlayerAction.PlayState:
                    return String.Format("<playState>{0}</playState>\0", actionContent);
                case PlayerAction.Volume:
                    return String.Format("<volume>{0}</volume>\0", actionContent);
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
            return result;
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
                        case "next":
                            SocketServer.Send(GetActionString(PlayerAction.Next,
                                                                 SocketServer._plugin.PlayerPlayNextTrack()));
                            break;
                        case "previous":
                            SocketServer.Send(SocketServer._clientSocket,
                                 GetActionString(PlayerAction.Previous,
                                                                 SocketServer._plugin.PlayerPlayPreviousTrack()));
                            break;
                        case "playPause":
                            SocketServer.Send(SocketServer._clientSocket,
                                 GetActionString(PlayerAction.PlayState,
                                                                 SocketServer._plugin.PlayerPlayPauseTrack()));
                            break;
                        case "playState":
                            SocketServer.Send(SocketServer._clientSocket,
                                 GetActionString(PlayerAction.PlayState, SocketServer._plugin.PlayerPlayState()));
                            break;
                        case "volume":
                            SocketServer.Send(SocketServer._clientSocket,
                                 GetActionString(PlayerAction.Volume,
                                                                 SocketServer._plugin.PlayerVolume(xmNode.InnerText)));
                            break;
                        case "songChanged":
                            SocketServer.Send(SocketServer._clientSocket,
                                 GetActionString(PlayerAction.SongChangedStatus,
                                                                 SocketServer._plugin.SongChanged.ToString(CultureInfo.InvariantCulture)));
                            SocketServer._plugin.SongChanged = false;
                            break;
                        case "songInfo":
                            SocketServer.Send(SocketServer._clientSocket,
                                 GetActionString(PlayerAction.SongInformation, SocketServer.GetSongInfo()));
                            break;
                        case "songCover":
                            new Thread(
                                () =>
                                SocketServer.Send(SocketServer._clientSocket,
                                     String.Format("<songCover>{0}</songCover>\0", SocketServer._plugin.GetCurrentTrackCover()))).
                                Start();
                            break;
                        case "stopPlayback":
                            SocketServer._plugin.PlayerStopPlayback();
                            SocketServer.Send(SocketServer._clientSocket, "<stopPlayback>{0}</stopPlayback>\0");
                            break;
                        case "shuffle":
                            SocketServer.Send(SocketServer._clientSocket,
                                 String.Format("<shuffle>{0}</shuffle>\0", SocketServer._plugin.PlayerShuffleState(xmNode.InnerText)));
                            break;
                        case "mute":
                            SocketServer.Send(SocketServer._clientSocket,
                                 String.Format("<mute>{0}</mute>\0", SocketServer._plugin.PlayerMuteState(xmNode.InnerText)));
                            break;
                        case "repeat":
                            SocketServer.Send(SocketServer._clientSocket,
                                 String.Format("<repeat>{0}</repeat>\0", SocketServer._plugin.PlayerRepeatState(xmNode.InnerText)));
                            break;
                        case "playlist":
                            SocketServer.Send(SocketServer._clientSocket, String.Format("<playlist>{0}</playlist>\0", SocketServer._plugin.PlaylistGetTracks()));
                            break;
                        case "playNow":
                            SocketServer._plugin.PlaylistGoToSpecifiedTrack(xmNode.InnerText);
                            SocketServer.Send(SocketServer._clientSocket, "<playNow/>\0");
                            break;
                        case "scrobbler":
                            SocketServer.Send(SocketServer._clientSocket,
                                 String.Format("<scrobbler>{0}</scrobbler>\0", SocketServer._plugin.ScrobblerState(xmNode.InnerText)));
                            break;
                        case "lyrics":
                            new Thread(
                                () =>
                                SocketServer.Send(SocketServer._clientSocket,
                                     String.Format("<lyrics>{0}</lyrics>\0", SocketServer._plugin.RetrieveCurrentTrackLyrics()))).
                                Start();
                            break;
                        case "rating":
                            SocketServer.Send(SocketServer._clientSocket,
                                 String.Format("<rating>{0}</rating>\0", SocketServer._plugin.TrackRating(xmNode.InnerText)));
                            break;
                        case "playerStatus":
                            SocketServer.Send(SocketServer._clientSocket,
                                 GetActionString(PlayerAction.PlayerStatus, SocketServer.GetPlayerStatus()));
                            break;
                    }
                    SocketServer.Send(SocketServer._clientSocket, String.Format("\r\n"));
                }
                catch (ThreadAbortException ex)
                {
                    ErrorHandler.LogError(ex);
                }
                catch
                {
                    try
                    {
                        SocketServer.Send(SocketServer._clientSocket, "<error/>\0");
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
