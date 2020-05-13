using System;
using System.Timers;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events.Notifications;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Monitoring
{
    public class PlayerStateMonitor : IPlayerStateMonitor, IDisposable
    {
        private readonly PlayerStateModel _stateModel;
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        private Timer _timer;
        private TinyMessageSubscriptionToken _volumeSubscriptionToken;
        private TinyMessageSubscriptionToken _muteSubscriptionToken;
        private TinyMessageSubscriptionToken _playStateSubscriptionToken;
        private TinyMessageSubscriptionToken _playingListSubscriptionToken;

        private bool _isDisposed;

        public PlayerStateMonitor(PlayerStateModel stateModel, ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _stateModel = stateModel;
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Start()
        {
            _volumeSubscriptionToken = _hub.Subscribe<VolumeLevelChangedEvent>(_ => { SendVolume(); });
            _muteSubscriptionToken = _hub.Subscribe<VolumeMuteChangedEvent>(_ => { SendMuteState(); });
            _playStateSubscriptionToken = _hub.Subscribe<PlayStateChangedEvent>(_ => { SendPlayState(); });
            _playingListSubscriptionToken = _hub.Subscribe<NowPlayingListChangedEvent>(_ => { });

            _timer = new Timer { Interval = 1000 };
            _timer.Elapsed += HandleTimerElapsed;
            _timer.Enabled = true;
        }

        public void Terminate()
        {
            _timer.Enabled = false;
            _timer.Stop();
            _timer.Close();
            _hub.Unsubscribe<VolumeLevelChangedEvent>(_volumeSubscriptionToken);
            _hub.Unsubscribe<VolumeMuteChangedEvent>(_muteSubscriptionToken);
            _hub.Unsubscribe<PlayStateChangedEvent>(_playStateSubscriptionToken);
            _hub.Unsubscribe<NowPlayingListChangedEvent>(_playingListSubscriptionToken);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                _timer.Dispose();
                _volumeSubscriptionToken.Dispose();
                _muteSubscriptionToken.Dispose();
                _playStateSubscriptionToken.Dispose();
                _playingListSubscriptionToken.Dispose();
            }

            _isDisposed = true;
        }

        private void SendVolume()
        {
            var playerMessage = new SocketMessage(Constants.PlayerVolume, _apiAdapter.GetVolume());
            _hub.Publish(new PluginResponseAvailableEvent(playerMessage));
        }

        private void SendMuteState()
        {
            var muteMessages = new SocketMessage(Constants.PlayerMute, _apiAdapter.IsMuted());
            _hub.Publish(new PluginResponseAvailableEvent(muteMessages));
        }

        private void SendPlayState()
        {
            var stateMessage = new SocketMessage(Constants.PlayerState, _apiAdapter.GetState());
            _hub.Publish(new PluginResponseAvailableEvent(stateMessage));
        }

        private void HandleTimerElapsed(object sender, ElapsedEventArgs args)
        {
            if (_apiAdapter.GetShuffleState() != _stateModel.Shuffle)
            {
                _stateModel.Shuffle = _apiAdapter.GetShuffleState();
                var message = new SocketMessage(Constants.PlayerShuffle, _stateModel.Shuffle);
                _hub.Publish(new PluginResponseAvailableEvent(message));
            }

            if (_apiAdapter.ScrobblingEnabled() != _stateModel.Scrobble)
            {
                _stateModel.Scrobble = _apiAdapter.ScrobblingEnabled();
                var message = new SocketMessage(Constants.PlayerScrobble, _stateModel.Scrobble);
                _hub.Publish(new PluginResponseAvailableEvent(message));
            }

            if (_apiAdapter.GetRepeatMode() != _stateModel.RepeatMode)
            {
                _stateModel.RepeatMode = _apiAdapter.GetRepeatMode();
                var message = new SocketMessage(Constants.PlayerRepeat, _stateModel.RepeatMode);
                _hub.Publish(new PluginResponseAvailableEvent(message));
            }
        }
    }
}
