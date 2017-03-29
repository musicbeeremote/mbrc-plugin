using System.Timers;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Core.Events.Notifications;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using TinyMessenger;

namespace MusicBeeRemoteCore.Monitoring
{
    class PlayerStateMonitor : IPlayerStateMonitor
    {
        private readonly PlayerStateModel _stateModel;
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        private Timer _timer;

        public PlayerStateMonitor(PlayerStateModel stateModel, ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _stateModel = stateModel;
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Start()
        {
            _hub.Subscribe<VolumeLevelChangedEvent>(_ =>
            {
                var playerMessage = new SocketMessage(Constants.PlayerVolume, _apiAdapter.GetVolume());
                _hub.Publish(new PluginResponseAvailableEvent(playerMessage));
            });

            _hub.Subscribe<VolumeMuteChangedEvent>(_ =>
            {
                var muteMessages = new SocketMessage(Constants.PlayerMute, _apiAdapter.IsMuted());
                _hub.Publish(new PluginResponseAvailableEvent(muteMessages));
            })

            _hub.Subscribe<PlayStateChangedEvent>(_ =>
            {
                var stateMessage = new SocketMessage(Constants.PlayerState, _apiAdapter.GetState());
                _hub.Publish(new PluginResponseAvailableEvent(stateMessage));
            })

            _timer = new Timer {Interval = 1000};
            _timer.Elapsed += HandleTimerElapsed;
            _timer.Enabled = true;
        }

        public void Stop()
        {
            _timer.Enabled = false;
            _timer.Stop();
            _timer.Close();
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