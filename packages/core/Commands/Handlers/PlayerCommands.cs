using System;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Events.Extensions;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Utilities.Network;

namespace MusicBeePlugin.Commands.Handlers
{
    /// <summary>
    ///     Player control commands using delegate pattern with dependency injection
    /// </summary>
    public class PlayerCommands
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IPluginLogger _logger;
        private readonly IPlayerDataProvider _playerDataProvider;
        private readonly IProtocolCapabilities _protocolCapabilities;

        public PlayerCommands(IPlayerDataProvider playerDataProvider, IPluginLogger logger, IEventAggregator eventAggregator,
            IProtocolCapabilities protocolCapabilities)
        {
            _playerDataProvider = playerDataProvider;
            _logger = logger;
            _eventAggregator = eventAggregator;
            _protocolCapabilities = protocolCapabilities;
        }

        /// <summary>
        ///     Start playback command
        /// </summary>
        public bool HandlePlay(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "play", context.ShortId);
                var result = _playerDataProvider.Play();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerPlay,
                    result,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute play command");
                return false;
            }
        }

        /// <summary>
        ///     Pause playback command
        /// </summary>
        public bool HandlePause(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "pause", context.ShortId);
                var result = _playerDataProvider.Pause();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerPause,
                    result,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute pause command");
                return false;
            }
        }

        /// <summary>
        ///     Toggle play/pause command
        /// </summary>
        public bool HandlePlayPause(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "play/pause", context.ShortId);

                // Execute the play/pause and get the result
                var result = _playerDataProvider.PlayPause();

                // Send response using new EventAggregator pattern, replicating RequestPlayPauseTrack
                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerPlayPause,
                    result,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute play/pause command");
                return false;
            }
        }

        /// <summary>
        ///     Stop playback command
        /// </summary>
        public bool HandleStop(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "stop", context.ShortId);
                var result = _playerDataProvider.StopPlayback();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerStop,
                    result,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute stop command");
                return false;
            }
        }

        /// <summary>
        ///     Play next track command
        /// </summary>
        public bool HandleNext(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "next", context.ShortId);
                var result = _playerDataProvider.PlayNext();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerNext,
                    result,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute next command");
                return false;
            }
        }

        /// <summary>
        ///     Play previous track command
        /// </summary>
        public bool HandlePrevious(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "previous", context.ShortId);
                var result = _playerDataProvider.PlayPrevious();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerPrevious,
                    result,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute previous command");
                return false;
            }
        }

        /// <summary>
        ///     Set volume command
        /// </summary>
        public bool HandleVolumeSet(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "volume set", context.ShortId);

                var result = true;
                if (context.TryGetData<int>(out var volume))
                    result = _playerDataProvider.SetVolume(volume);

                var currentVolume = _playerDataProvider.GetVolume();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerVolume,
                    currentVolume,
                    context.ConnectionId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute volume set command");
                return false;
            }
        }

        /// <summary>
        ///     Set mute state command
        /// </summary>
        public bool HandleMuteSet(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "mute set", context.ShortId);

                bool result;

                if (IsToggleAction(context))
                {
                    // Toggle current mute state
                    var currentMute = _playerDataProvider.GetMute();
                    result = _playerDataProvider.SetMute(!currentMute);
                }
                else if (context.TryGetData<bool>(out var mute))
                {
                    // Set specific mute state
                    result = _playerDataProvider.SetMute(mute);
                }
                else
                {
                    // Just return current state (state inquiry)
                    result = true;
                }

                var currentMuteState = _playerDataProvider.GetMute();
                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerMute,
                    currentMuteState,
                    context.ConnectionId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute mute set command");
                return false;
            }
        }

        /// <summary>
        ///     Handle shuffle command - supports both simple shuffle and AutoDJ based on client protocol
        /// </summary>
        public bool HandleShuffle(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "shuffle", context.ShortId);

                var stateAction = GetStateAction(context);

                return _protocolCapabilities.SupportsAutoDjShuffle(context.ConnectionId)
                    ? HandleAutoDjShuffle(context, stateAction)
                    : HandleSimpleShuffle(context, stateAction);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute shuffle command");
                return false;
            }
        }

        private bool HandleSimpleShuffle(ICommandContext context, StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                var currentShuffle = _playerDataProvider.GetShuffle();
                _playerDataProvider.SetShuffle(!currentShuffle);
            }
            else if (context.TryGetData<bool>(out var shuffleValue))
            {
                _playerDataProvider.SetShuffle(shuffleValue);
            }
            else
            {
                var shuffleState = _playerDataProvider.GetShuffleState();
                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerShuffle,
                    shuffleState,
                    context.ConnectionId);
            }


            return true;
        }

        private bool HandleAutoDjShuffle(ICommandContext context, StateAction action)
        {
            if (action == StateAction.Toggle)
            {
                HandleAutoDjToggle();
            }
            else if (context.TryGetData<ShuffleState>(out var targetState))
            {
                SetAutoDjState(targetState);
            }
            else
            {
                var newState = _playerDataProvider.GetShuffleState();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerShuffle,
                    newState,
                    context.ConnectionId);
            }

            return true;
        }

        private ShuffleState HandleAutoDjToggle()
        {
            var shuffleEnabled = _playerDataProvider.GetShuffle();
            var autoDjEnabled = _playerDataProvider.GetAutoDjEnabled();

            if (shuffleEnabled && !autoDjEnabled)
            {
                // Shuffle is on, AutoDJ is off -> Start AutoDJ
                var success = _playerDataProvider.SetAutoDj(true);
                return success ? ShuffleState.AutoDj : _playerDataProvider.GetShuffleState();
            }

            if (autoDjEnabled)
            {
                // AutoDJ is on -> Turn off AutoDJ
                _playerDataProvider.SetAutoDj(false);
                return ShuffleState.Off;
            }

            {
                // Both are off -> Turn on shuffle
                var success = _playerDataProvider.SetShuffle(true);
                return success ? ShuffleState.Shuffle : ShuffleState.Off;
            }
        }

        private ShuffleState SetAutoDjState(ShuffleState targetState)
        {
            switch (targetState)
            {
                case ShuffleState.Off:
                    _playerDataProvider.SetShuffle(false);
                    _playerDataProvider.SetAutoDj(false);
                    return ShuffleState.Off;

                case ShuffleState.Shuffle:
                    _playerDataProvider.SetAutoDj(false);
                    var shuffleSuccess = _playerDataProvider.SetShuffle(true);
                    return shuffleSuccess ? ShuffleState.Shuffle : _playerDataProvider.GetShuffleState();

                case ShuffleState.AutoDj:
                    _playerDataProvider.SetShuffle(true);
                    var autoDjSuccess = _playerDataProvider.SetAutoDj(true);
                    return autoDjSuccess ? ShuffleState.AutoDj : _playerDataProvider.GetShuffleState();

                default:
                    return _playerDataProvider.GetShuffleState();
            }
        }


        /// <summary>
        ///     Helper method to determine if the event data represents a toggle action
        /// </summary>
        private static bool IsToggleAction(ICommandContext context)
        {
            return context.GetDataOrDefault<string>() == "toggle";
        }

        /// <summary>
        ///     Helper method to get StateAction from event data
        /// </summary>
        private static StateAction GetStateAction(ICommandContext context)
        {
            return IsToggleAction(context) ? StateAction.Toggle : StateAction.State;
        }

        /// <summary>
        ///     Handle scrobble command - supports toggle and direct boolean setting
        /// </summary>
        public bool HandleScrobble(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "scrobble", context.ShortId);

                var stateAction = GetStateAction(context);

                if (stateAction == StateAction.Toggle)
                {
                    var currentScrobble = _playerDataProvider.GetScrobbleEnabled();
                    _playerDataProvider.SetScrobble(!currentScrobble);
                }
                else if (context.TryGetData<bool>(out var scrobbleValue))
                {
                    _playerDataProvider.SetScrobble(scrobbleValue);
                    var scrobbleState = _playerDataProvider.GetScrobbleEnabled();
                    _eventAggregator.PublishMessage(
                        ProtocolConstants.PlayerScrobble,
                        scrobbleState,
                        context.ConnectionId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute scrobble command");
                return false;
            }
        }

        /// <summary>
        ///     Handle AutoDJ command - supports toggle and direct boolean setting
        /// </summary>
        public bool HandleAutoDj(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "AutoDJ", context.ShortId);

                var stateAction = GetStateAction(context);

                if (stateAction == StateAction.Toggle)
                {
                    var currentAutoDj = _playerDataProvider.GetAutoDjEnabled();
                    _playerDataProvider.SetAutoDj(!currentAutoDj);
                }
                else if (context.TryGetData<bool>(out var autoDjValue))
                {
                    _playerDataProvider.SetAutoDj(autoDjValue);
                }
                else
                {
                    var autoDjState = _playerDataProvider.GetAutoDjEnabled();
                    _eventAggregator.PublishMessage(
                        ProtocolConstants.PlayerAutoDj,
                        autoDjState,
                        context.ConnectionId);
                }


                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute AutoDJ command");
                return false;
            }
        }

        /// <summary>
        ///     Handle repeat command - supports toggle and direct RepeatMode setting
        /// </summary>
        public bool HandleRepeat(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} command for client {ClientId}", "repeat", context.ShortId);

                var stateAction = GetStateAction(context);

                if (stateAction == StateAction.Toggle)
                {
                    var currentRepeat = _playerDataProvider.GetRepeatMode();
                    RepeatMode newRepeat;
                    switch (currentRepeat)
                    {
                        case RepeatMode.None:
                            newRepeat = RepeatMode.All;
                            break;
                        case RepeatMode.All:
                            newRepeat = RepeatMode.One;
                            break;
                        case RepeatMode.One:
                            newRepeat = RepeatMode.None;
                            break;
                        default:
                            newRepeat = RepeatMode.None;
                            break;
                    }

                    _playerDataProvider.SetRepeatMode(newRepeat);
                }
                else if (context.TryGetData<RepeatMode>(out var repeatMode))
                {
                    _playerDataProvider.SetRepeatMode(repeatMode);
                }
                else
                {
                    var repeatState = _playerDataProvider.GetRepeatMode();
                    _eventAggregator.PublishMessage(
                        ProtocolConstants.PlayerRepeat,
                        repeatState,
                        context.ConnectionId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute repeat command");
                return false;
            }
        }

        /// <summary>
        ///     Handle player status request - returns comprehensive player state
        /// </summary>
        public bool HandlePlayerStatus(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} request for client {ClientId}", "player status", context.ShortId);

                var isLegacyClient = !_protocolCapabilities.SupportsFullPlayerStatus(context.ConnectionId);
                var status = _playerDataProvider.GetPlayerStatus(isLegacyClient);

                _logger.Debug("Player status: {Status} for client {ClientId}", status, context.ShortId);
                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerStatus,
                    status,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute player status request");
                return false;
            }
        }

        /// <summary>
        ///     Handle output device request - returns available output devices and active device
        /// </summary>
        public bool HandleOutputDevices(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} request for client {ClientId}", "output devices", context.ShortId);

                var outputDevice = _playerDataProvider.GetOutputDevices();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerOutput,
                    outputDevice,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute output devices request");
                return false;
            }
        }

        /// <summary>
        ///     Handle output device switch - switches to specified device and returns updated device list
        /// </summary>
        public bool HandleOutputDeviceSwitch(ICommandContext context)
        {
            try
            {
                _logger.Debug("Processing {Command} for client {ClientId}", "output device switch", context.ShortId);

                var deviceName = context.GetDataOrDefault<string>();
                if (string.IsNullOrEmpty(deviceName))
                {
                    _logger.Warn("No device name provided for output switch");
                    return false;
                }

                // Switch to the new device
                _playerDataProvider.SetOutputDevice(deviceName);

                // Get and return the updated device list
                var outputDevice = _playerDataProvider.GetOutputDevices();

                _eventAggregator.PublishMessage(
                    ProtocolConstants.PlayerOutput,
                    outputDevice,
                    context.ConnectionId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute output device switch");
                return false;
            }
        }
    }
}
