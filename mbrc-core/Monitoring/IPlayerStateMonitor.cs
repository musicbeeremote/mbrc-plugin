using System.Timers;
using MusicBeeRemoteCore.ApiAdapters;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using TinyMessenger;

namespace MusicBeeRemoteCore.Monitoring
{
    public interface IPlayerStateMonitor : IStateMonitor
    {

    }

    public interface IStateMonitor
    {
        void Start();
        void Stop();
    }

    class PlayerPlayerStateMonitor : IPlayerStateMonitor
    {
        private readonly PlayerStateModel _stateModel;
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerStateAdapter _stateAdapter;
        private Timer _timer;

        public PlayerPlayerStateMonitor(PlayerStateModel stateModel, ITinyMessengerHub hub, IPlayerStateAdapter stateAdapter)
        {
            _stateModel = stateModel;
            _hub = hub;
            _stateAdapter = stateAdapter;
        }

        public void Start()
        {
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
            if (_stateAdapter.GetShuffleState() != _stateModel.Shuffle)
            {
                _stateModel.Shuffle = _stateAdapter.GetShuffleState();
                var message = new SocketMessage(Constants.PlayerShuffle, _stateModel.Shuffle);
                _hub.Publish(new PluginResponseAvailableEvent(message));
            }

            if (_stateAdapter.ScrobblingEnabled() != _stateModel.Scrobble)
            {
                _stateModel.Scrobble = _stateAdapter.ScrobblingEnabled();
                var message = new SocketMessage(Constants.PlayerScrobble, _stateModel.Scrobble);
                _hub.Publish(new PluginResponseAvailableEvent(message));
            }

            if (_stateAdapter.GetRepeatMode() != _stateModel.RepeatMode)
            {
                _stateModel.RepeatMode = _stateAdapter.GetRepeatMode();
                var message = new SocketMessage(Constants.PlayerRepeat, _stateModel.RepeatMode);
                _hub.Publish(new PluginResponseAvailableEvent(message));
            }
        }
    }
}