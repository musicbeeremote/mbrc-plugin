using System;
using System.Globalization;
using MusicBeePlugin.Events;
using MusicBeePlugin.Model;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Utilities;

namespace MusicBeePlugin.Controller
{
    internal class RemoteController
    {
        private readonly PlayerStateModel _playerStateModel;
        private Plugin _plugin;

        private static readonly RemoteController ClassInstance = new RemoteController();

        public static RemoteController Instance
        {
            get { return ClassInstance; }
        }

        private readonly SocketServer _server;
        private readonly ProtocolHandler _pHandler;

        private RemoteController()
        {
            _playerStateModel = new PlayerStateModel();
            _server = new SocketServer();
            _pHandler = new ProtocolHandler();

            _pHandler.ReplyAvailable += HandleReplyAvailable;
            _pHandler.DisconnectClient += HandleDisconnectClient;
            _pHandler.RequestAvailable += HandleRequestAvailable;
            _server.ClientConnected += HandleClientConnected;
            _server.ClientDisconnected += HandleClientDisconnected;
            _server.DataAvailable += HandleServerDataAvailable;
            _playerStateModel.ModelStateEvent += HandleModelStateChange;
        }

        public void StartSocket()
        {
            _server.Start();
        }

        public void StopSocket()
        {
            _server.Stop();
        }

        private void HandleDisconnectClient(object sender, MessageEventArgs e)
        {
            _server.HandleDisconnectClient(sender, e);
        }

        private void HandleReplyAvailable(object sender, MessageEventArgs e)
        {
            _server.HandleReplyAvailable(sender, e);
        }

        private void HandleClientDisconnected(object sender, MessageEventArgs e)
        {
            Authenticator.RemoveClientOnDisconnect(e);
        }

        private void HandleClientConnected(object sender, MessageEventArgs e)
        {
            Authenticator.AddClientOnConnect(e);
        }

        private void HandleServerDataAvailable(object sender, MessageEventArgs e)
        {
            _pHandler.ProcessIncomingMessage(e.Message, e.ClientId);
        }

        private void HandleRequestAvailable(object sender, ClientRequestArgs e)
        {
            switch (e.Type)
            {
                case RequestType.PlayNext:
                    _plugin.RequestNextTrack(e.ClientId);
                    break;
                case RequestType.PlayPrevious:
                    _plugin.RequestPreviousTrack(e.ClientId);
                    break;
                case RequestType.PlayPause:
                    _plugin.RequestPlayPauseTrack(e.ClientId);
                    break;
                case RequestType.Stop:
                    _plugin.RequestStopPlayback(e.ClientId);
                    break;
                case RequestType.Volume:
                    if(!String.IsNullOrEmpty(e.RequestData))
                    {
                        int iVolume;
                        if (int.TryParse(e.RequestData, out iVolume))
                        {
                            if (iVolume >= 0 && iVolume <= 100)
                            {
                                _plugin.RequestVolumeChange(iVolume);
                            }
                        }
                    }
                    else
                    {
                        _pHandler.VolumeLevelChanged(_playerStateModel.Volume.ToString(CultureInfo.InvariantCulture),e.ClientId);
                    }
                    break;
                case RequestType.PlayerStatus:
                    break;
                case RequestType.PlayState:
                    _pHandler.PlayStateRequestHandled(_playerStateModel.PlayState, e.ClientId);
                    break;
                case RequestType.SongInformation:
                    _pHandler.SongInfomationRequestHandled(_playerStateModel.Track, e.ClientId);
                    break;
                case RequestType.SongCover:
                    _pHandler.SongCoverRequestHandled(_playerStateModel.Cover, e.ClientId);
                    break;
                case RequestType.Playlist:
                    _plugin.RequestNowPlayingList(1.0,e.ClientId);
                    break;
                case RequestType.Lyrics:
                    _pHandler.LyricsRequestHandled(_playerStateModel.Lyrics, e.ClientId);
                    break;
                case RequestType.ScrobblerState:
                    _plugin.RequestScrobblerState(e.RequestData.Contains("toggle") ? StateAction.Toggle : StateAction.State);
                    break;
                case RequestType.ShuffleState:
                    _plugin.RequestShuffleState(e.RequestData.Contains("toggle") ? StateAction.Toggle : StateAction.State);
                    break;
                case RequestType.RepeatState:
                    break;
                case RequestType.MuteState:
                    _plugin.RequestMuteState(e.RequestData.Contains("toggle") ? StateAction.Toggle : StateAction.State);
                    break;
                case RequestType.PlayNow:
                    break;
                case RequestType.Rating:
                    if(String.IsNullOrEmpty(e.RequestData))
                    {
                        _pHandler.RatingRequestHandled(_playerStateModel.TrackRating,e.ClientId);
                        }
                    else
                    {
                        _plugin.RequestTrackRating(e.RequestData,e.ClientId);
                    }
                    
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Initialize(Plugin plugin)
        {
            _plugin = plugin;
            _plugin.PlayerStateChanged += HandlePlayerStateEvent;
            _plugin.RequestVolumeChange(-1);
            _plugin.RequestMuteState(StateAction.State);
            _plugin.RequestRepeatState(StateAction.State);
            _plugin.RequestShuffleState(StateAction.State);
            _plugin.RequestScrobblerState(StateAction.State);
            _plugin.RequestNowPlayingTrackCover();
            _plugin.RequestNowPlayingTrackLyrics();
            _plugin.RequestTrackRating(String.Empty, -1);
        }

        private void HandlePlayerStateEvent(object sender, DataEventArgs e)
        {
            switch (e.Type)
            {
                case EventDataType.Track:
                    _playerStateModel.Track = e.TrackData;
                    _plugin.RequestNowPlayingTrackCover();
                    _plugin.RequestNowPlayingTrackLyrics();
                    break;
                case EventDataType.PlayState:
                    _playerStateModel.SetPlayState(e.PlayState);
                    break;
                case EventDataType.Volume:
                    _playerStateModel.SetVolume(e.FloatData);
                    break;
                case EventDataType.ShuffleState:
                    _playerStateModel.ShuffleState = e.BoolData;
                    break;
                case EventDataType.RepeatMode:
                    _playerStateModel.RepeatMode = e.RepeatMode;
                    break;
                case EventDataType.MuteState:
                    _playerStateModel.MuteState = e.BoolData;
                    break;
                case EventDataType.ScrobblerState:
                    _playerStateModel.ScrobblerState = e.BoolData;
                    break;
                case EventDataType.TrackRating:
                    _playerStateModel.TrackRating = e.StringData;
                    break;
                case EventDataType.Lyrics:
                    _playerStateModel.Lyrics = e.StringData;
                    break;
                case EventDataType.Cover:
                    _playerStateModel.Cover = e.StringData;
                    break;
                case EventDataType.NextTrackRequest:
                    _pHandler.NextTrackRequestHandled(e.StringData, e.ClientId);
                    break;
                case EventDataType.StopRequest:
                    _pHandler.StopRequestHandled(e.StringData, e.ClientId);
                    break;
                case EventDataType.PlayPauseRequest:
                    _pHandler.PlayPauseRequestHandled(e.StringData, e.ClientId);
                    break;
                 case EventDataType.PreviousTrackRequest:
                    _pHandler.PreviousTrackRequestHandled(e.StringData, e.ClientId);
                 break;
                    case EventDataType.Playlist:
                    _pHandler.PlaylistRequestHandled(e.StringData, e.ClientId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void HandleModelStateChange(object sender, DataEventArgs e)
        {
            switch (e.Type)
            {
                case EventDataType.Track:
                    _pHandler.SongInfomationRequestHandled(_playerStateModel.Track, -1);
                    break;
                case EventDataType.Cover:
                    _pHandler.SongCoverRequestHandled(_playerStateModel.Cover, -1);
                    break;
                case EventDataType.Lyrics:
                    break;
                case EventDataType.PlayState:
                    _pHandler.PlayStateChanged(_playerStateModel.PlayState.ToString(CultureInfo.InvariantCulture), -1);
                    break;
                case EventDataType.Volume:
                    _pHandler.VolumeLevelChanged(_playerStateModel.Volume.ToString(CultureInfo.InvariantCulture), -1);
                    break;
                case EventDataType.ShuffleState:
                    _pHandler.ShuffleStateChanged(_playerStateModel.ShuffleState.ToString(CultureInfo.InvariantCulture), -1);
                    break;
                case EventDataType.RepeatMode:
                    _pHandler.RepeatStateChanged(_playerStateModel.RepeatMode.ToString(),-1);
                    break;
                case EventDataType.MuteState:
                    _pHandler.MuteStateChanged(_playerStateModel.MuteState.ToString(CultureInfo.InvariantCulture), -1);
                    break;
                case EventDataType.ScrobblerState:
                    _pHandler.ScrobbleStateChanged(_playerStateModel.ScrobblerState.ToString(CultureInfo.InvariantCulture), -1);
                    break;
                case EventDataType.TrackRating:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}