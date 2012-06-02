using System;
using System.Threading;
using MusicBeePlugin.Entities;
using MusicBeePlugin.Events;
using MusicBeePlugin.Model;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Utilities;

namespace MusicBeePlugin.Controller
{
    class RemoteController
    {
        private PlayerStateModel _playerStateModel;
        private Plugin _plugin;

        private static readonly RemoteController ClassInstance = new RemoteController();
        public static RemoteController Instance { get { return ClassInstance; } }

        private SocketServer _server;
        private ProtocolHandler _pHandler;

        private void HandleDisconnectClient(object sender, MessageEventArgs e)
        {
           _server.HandleDisconnectClient(sender,e);
        }

        private void HandleReplyAvailable(object sender, MessageEventArgs e)
        {
           _server.HandleReplyAvailable(sender,e);
        }

        private void HandleClientDisconnected(object sender, MessageEventArgs e)
        {
           Authenticator.RemoveClientOnDisconnect(e);
        }

        private void HandleClientConnected(object sender, MessageEventArgs e)
        {
           Authenticator.AddClientOnConnect(e);
        }

        public void StartSocket()
        {
            _server.Start();
        }

        public void StopSocket()
        {
            _server.Stop();
        }

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

        private void HandleServerDataAvailable(object sender, MessageEventArgs e)
        {
            _pHandler.ProcessIncomingMessage(e.Message, e.ClientId);
        }

        private void HandleRequestAvailable(object sender, ClientRequestArgs e)
        {
            switch(e.Type)
            {
                case RequestType.PlayNext:
                    _pHandler.NextTrackRequestHandled(_plugin.PlayerPlayNextTrack(), e.ClientId);
                    break;
                case RequestType.PlayPrevious:
                    _pHandler.PreviousTrackRequestHandled(_plugin.PlayerPlayPreviousTrack(), e.ClientId);
                    break;
                case RequestType.PlayPause:
                    _pHandler.PlayPauseRequestHandled(_plugin.PlayerPlayPauseTrack(), e.ClientId);
                    break;
                case RequestType.Stop:
                    _pHandler.StopRequestHandled(_plugin.PlayerStopPlayback(),e.ClientId);
                    break;
                case RequestType.Volume:
                    _plugin.PlayerVolume(e.RequestData);
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
                    break;
                case RequestType.Lyrics:
                    _pHandler.LyricsRequestHandled(_playerStateModel.Lyrics, e.ClientId);
                    break;
                case RequestType.ScrobblerState:
                    //_pHandler.ScrobbleStateChanged(_playerStateModel.ScrobblerState, e.ClientId);
                    break;
                case RequestType.ShuffleState:
                    break;
                case RequestType.RepeatState:
                    break;
                case RequestType.MuteState:
                    break;
                case RequestType.PlayNow:
                    break;
                case RequestType.Rating:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Initialize(Plugin plugin)
        {
            _plugin = plugin;
            _plugin.PlayerStateChanged += HandlePlayerStateEvent;
        }

        private void HandlePlayerStateEvent(object sender, DataEventArgs e)
        {
            switch (e.Type)
            {
                case DataType.Track:
                    TrackInfo track = new TrackInfo();
                    track.Artist = _plugin.CurrentTrackArtist;
                    track.Title = _plugin.CurrentTrackTitle;
                    track.Album = _plugin.CurrentTrackAlbum;
                    track.Year = _plugin.CurrentTrackYear;
                    _playerStateModel.Track = track;
                    _playerStateModel.Cover = _plugin.CurrentTrackCover;
                    break;
                case DataType.PlayState:
                    _playerStateModel.setPlayState(_plugin.PlayerPlayState);
                    break;
                case DataType.Volume:
                    break;
                case DataType.ShuffleState:
                    break;
                case DataType.RepeatMode:
                    break;
                case DataType.MuteState:
                    break;
                case DataType.ScrobblerState:
                    break;
                case DataType.TrackRating:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void HandleModelStateChange(object sender, DataEventArgs e)
        {
            switch(e.Type)
            {
                case DataType.Track:
                   _pHandler.TrackChanged(_playerStateModel.Track, _playerStateModel.Cover, -1);
                    break;
                case DataType.Cover:
                    break;
                case DataType.Lyrics:
                    break;
                case DataType.PlayState:
                    break;
                case DataType.Volume:
                    break;
                case DataType.ShuffleState:
                    break;
                case DataType.RepeatMode:
                    break;
                case DataType.MuteState:
                    break;
                case DataType.ScrobblerState:
                    break;
                case DataType.TrackRating:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
