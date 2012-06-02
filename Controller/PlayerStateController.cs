using System;
using System.Threading;
using MusicBeePlugin.Events;
using MusicBeePlugin.Model;

namespace MusicBeePlugin.Controller
{
    class PlayerStateController
    {
        private PlayerStateModel _playerStateModel;
        private Plugin _plugin;

        private static readonly PlayerStateController ClassInstance = new PlayerStateController();
        public static PlayerStateController Instance { get { return ClassInstance; } }

        private PlayerStateController()
        {
           _playerStateModel = new PlayerStateModel();
            _playerStateModel.ModelStateEvent += HandleModelStateChange;
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
                    _playerStateModel.Artist = _plugin.CurrentTrackArtist;
                    _playerStateModel.Title = _plugin.CurrentTrackTitle;
                    _playerStateModel.Album = _plugin.CurrentTrackAlbum;
                    _playerStateModel.Year = _plugin.CurrentTrackYear;
                    new Thread(() => _playerStateModel.Cover = _plugin.CurrentTrackCover);
                    new Thread(() => _playerStateModel.Lyrics = _plugin.CurrentTrackLyrics);
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
                    break;
                case DataType.Artist:
                    break;
                case DataType.Album:
                    break;
                case DataType.Title:
                    break;
                case DataType.Year:
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
