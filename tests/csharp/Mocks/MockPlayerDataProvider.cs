using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeeRemote.Core.Tests.Mocks
{
    /// <summary>
    ///     Mock implementation of IPlayerDataProvider for testing.
    ///     Allows verification of method calls and configuration of return values.
    /// </summary>
    public class MockPlayerDataProvider : IPlayerDataProvider
    {
        // Configurable state
        public PlayState CurrentPlayState { get; set; } = PlayState.Stopped;
        public int CurrentVolume { get; set; } = 50;
        public bool IsMuted { get; set; }
        public bool ShuffleEnabled { get; set; }
        public RepeatMode CurrentRepeatMode { get; set; } = RepeatMode.None;
        public bool AutoDjEnabled { get; set; }
        public bool ScrobblingEnabled { get; set; }
        public int CurrentPosition { get; set; }
        public string[] OutputDeviceNames { get; set; } = { "Default", "Speakers", "Headphones" };
        public string ActiveOutputDevice { get; set; } = "Default";

        // Call counters for verification
        public int PlayCallCount { get; private set; }
        public int PauseCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int PlayNextCallCount { get; private set; }
        public int PlayPreviousCallCount { get; private set; }
        public int PlayPauseCallCount { get; private set; }

        #region State Queries

        public PlayState GetPlayState() => CurrentPlayState;

        public ShuffleState GetShuffleState()
        {
            if (AutoDjEnabled)
                return ShuffleState.AutoDj;
            return ShuffleEnabled ? ShuffleState.Shuffle : ShuffleState.Off;
        }

        public bool GetShuffle() => ShuffleEnabled;

        public RepeatMode GetRepeatMode() => CurrentRepeatMode;

        public int GetVolume() => CurrentVolume;

        public bool GetMute() => IsMuted;

        public bool GetScrobbleEnabled() => ScrobblingEnabled;

        public bool GetAutoDjEnabled() => AutoDjEnabled;

        public int GetPosition() => CurrentPosition;

        #endregion

        #region Playback Control

        public bool Play()
        {
            PlayCallCount++;
            CurrentPlayState = PlayState.Playing;
            return true;
        }

        public bool Pause()
        {
            PauseCallCount++;
            CurrentPlayState = PlayState.Paused;
            return true;
        }

        public bool PlayPause()
        {
            PlayPauseCallCount++;
            if (CurrentPlayState == PlayState.Playing)
            {
                CurrentPlayState = PlayState.Paused;
            }
            else
            {
                CurrentPlayState = PlayState.Playing;
            }
            return true;
        }

        public bool StopPlayback()
        {
            StopCallCount++;
            CurrentPlayState = PlayState.Stopped;
            return true;
        }

        public bool PlayNext()
        {
            PlayNextCallCount++;
            return true;
        }

        public bool PlayPrevious()
        {
            PlayPreviousCallCount++;
            return true;
        }

        #endregion

        #region Settings

        public bool SetVolume(int volume)
        {
            if (volume < 0 || volume > 100)
                return false;
            CurrentVolume = volume;
            IsMuted = false; // Unmute when volume changes
            return true;
        }

        public bool SetMute(bool mute)
        {
            IsMuted = mute;
            return true;
        }

        public bool SetShuffle(bool enabled)
        {
            ShuffleEnabled = enabled;
            return true;
        }

        public bool SetRepeatMode(RepeatMode mode)
        {
            CurrentRepeatMode = mode;
            return true;
        }

        public bool SetScrobble(bool enabled)
        {
            ScrobblingEnabled = enabled;
            return true;
        }

        public bool SetAutoDj(bool enabled)
        {
            AutoDjEnabled = enabled;
            return true;
        }

        public bool SetPosition(int position)
        {
            CurrentPosition = position;
            return true;
        }

        #endregion

        #region Composite Status

        public PlayerStatus GetPlayerStatus(bool legacyShuffleFormat)
        {
            return new PlayerStatus
            {
                State = CurrentPlayState.ToString(),
                Mute = IsMuted,
                Volume = CurrentVolume.ToString(),
                Repeat = CurrentRepeatMode.ToString(),
                Shuffle = legacyShuffleFormat
                    ? (object)ShuffleEnabled
                    : GetShuffleState().ToString(),
                Scrobble = ScrobblingEnabled
            };
        }

        #endregion

        #region Output Devices

        public OutputDevice GetOutputDevices()
        {
            return new OutputDevice(OutputDeviceNames, ActiveOutputDevice);
        }

        public bool SetOutputDevice(string deviceName)
        {
            ActiveOutputDevice = deviceName;
            return true;
        }

        #endregion
    }
}
