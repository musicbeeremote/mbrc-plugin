using System;
using System.Timers;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Constants;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Protocol.Messages;

namespace MusicBeePlugin.Services.Core
{
    public class StateMonitor : IStateMonitor
    {
        private readonly IEventAggregator _eventAggregator;

        private readonly IPlayerDataProvider _playerDataProvider;
        private readonly ITrackDataProvider _trackDataProvider;
        private Timer _positionUpdateTimer;

        /// <summary>
        ///     Represents the current repeat mode.
        /// </summary>
        private RepeatMode _repeat;

        /// <summary>
        ///     The scrobble.
        /// </summary>
        private bool _scrobble;

        /// <summary>
        ///     The shuffle.
        /// </summary>
        private ShuffleState _shuffleState;

        /// <summary>
        ///     The timer.
        /// </summary>
        private Timer _stateTimer;

        public StateMonitor(IPlayerDataProvider playerDataProvider, ITrackDataProvider trackDataProvider, IEventAggregator eventAggregator)
        {
            _playerDataProvider = playerDataProvider;
            _trackDataProvider = trackDataProvider;
            _eventAggregator = eventAggregator;
        }

        public void StartMonitoring()
        {
            StartPlayStateMonitoring();
            StartStateMonitoring();
        }

        public void StopMonitoring()
        {
            StopPlayStateMonitoring();
            StopStateMonitoring();
        }

        private void PositionUpdateTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (_playerDataProvider.GetPlayState() != PlayState.Playing)
                return;
            var currentPlaybackPosition = _trackDataProvider.GetPlaybackPosition();
            var message = MessageSendEvent.Create(ProtocolConstants.NowPlayingPosition, currentPlaybackPosition);
            _eventAggregator.Publish(message);
        }

        /// <summary>
        ///     This function runs periodically every 1000 ms as the timer ticks and
        ///     checks for changes on the player status.  If a change is detected on
        ///     one of the monitored variables the function will fire an event with
        ///     the new status.
        /// </summary>
        /// <param name="sender">
        ///     The sender.
        /// </param>
        /// <param name="args">
        ///     The event arguments.
        /// </param>
        private void HandleStateTimerElapsed(object sender, ElapsedEventArgs args)
        {
            if (_playerDataProvider.GetShuffleState() != _shuffleState)
            {
                _shuffleState = _playerDataProvider.GetShuffleState();
                var message = MessageSendEvent.Create(ProtocolConstants.PlayerShuffle, _shuffleState);
                _eventAggregator.Publish(message);
            }

            if (_playerDataProvider.GetScrobbleEnabled() != _scrobble)
            {
                _scrobble = _playerDataProvider.GetScrobbleEnabled();
                var message = MessageSendEvent.Create(ProtocolConstants.PlayerScrobble, _scrobble);
                _eventAggregator.Publish(message);
            }

            if (_playerDataProvider.GetRepeatMode() != _repeat)
            {
                _repeat = _playerDataProvider.GetRepeatMode();
                var message = MessageSendEvent.Create(ProtocolConstants.PlayerRepeat, _repeat);
                _eventAggregator.Publish(message);
            }
        }

        private void StartStateMonitoring()
        {
            _scrobble = _playerDataProvider.GetScrobbleEnabled();
            _repeat = _playerDataProvider.GetRepeatMode();
            _shuffleState = _playerDataProvider.GetShuffleState();
            _stateTimer = new Timer { Interval = TimerConstants.StateCheckIntervalMs };
            _stateTimer.Elapsed += HandleStateTimerElapsed;
            _stateTimer.Enabled = true;
        }

        private void StartPlayStateMonitoring()
        {
            _positionUpdateTimer = new Timer(TimerConstants.PositionUpdateIntervalMs);
            _positionUpdateTimer.Elapsed += PositionUpdateTimerOnElapsed;
            _positionUpdateTimer.Enabled = true;
        }

        private void StopPlayStateMonitoring()
        {
            if (_positionUpdateTimer == null)
                return;
            _positionUpdateTimer.Enabled = false;
            _positionUpdateTimer.Elapsed -= PositionUpdateTimerOnElapsed;
            _positionUpdateTimer.Dispose();
            _positionUpdateTimer = null;
        }

        private void StopStateMonitoring()
        {
            if (_stateTimer == null)
                return;
            _stateTimer.Enabled = false;
            _stateTimer.Elapsed -= HandleStateTimerElapsed;
            _stateTimer.Dispose();
            _stateTimer = null;
        }

        #region IDisposable Implementation

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    StopMonitoring();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
