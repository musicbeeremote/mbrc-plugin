using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.DataProviders
{
    /// <summary>
    ///     Tests for IPlayerDataProvider interface behavior using mock implementation.
    ///     Tests verify the contract behavior that implementations must follow.
    /// </summary>
    public class PlayerDataProviderTests
    {
        private readonly MockPlayerDataProvider _provider;

        public PlayerDataProviderTests()
        {
            _provider = new MockPlayerDataProvider();
        }

        #region State Query Tests

        [Fact]
        public void GetPlayState_ReturnsCurrentState()
        {
            _provider.CurrentPlayState = PlayState.Playing;
            Assert.Equal(PlayState.Playing, _provider.GetPlayState());
        }

        [Fact]
        public void GetShuffleState_WhenAutoDjEnabled_ReturnsAutoDj()
        {
            _provider.AutoDjEnabled = true;
            _provider.ShuffleEnabled = false;

            Assert.Equal(ShuffleState.AutoDj, _provider.GetShuffleState());
        }

        [Fact]
        public void GetShuffleState_WhenShuffleEnabled_ReturnsShuffle()
        {
            _provider.AutoDjEnabled = false;
            _provider.ShuffleEnabled = true;

            Assert.Equal(ShuffleState.Shuffle, _provider.GetShuffleState());
        }

        [Fact]
        public void GetShuffleState_WhenBothDisabled_ReturnsOff()
        {
            _provider.AutoDjEnabled = false;
            _provider.ShuffleEnabled = false;

            Assert.Equal(ShuffleState.Off, _provider.GetShuffleState());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(100)]
        public void GetVolume_ReturnsConfiguredVolume(int volume)
        {
            _provider.CurrentVolume = volume;
            Assert.Equal(volume, _provider.GetVolume());
        }

        [Fact]
        public void GetMute_ReturnsMuteState()
        {
            _provider.IsMuted = true;
            Assert.True(_provider.GetMute());

            _provider.IsMuted = false;
            Assert.False(_provider.GetMute());
        }

        #endregion

        #region Playback Control Tests

        [Fact]
        public void Play_ChangesStateToPlaying()
        {
            _provider.CurrentPlayState = PlayState.Stopped;
            _provider.Play();

            Assert.Equal(PlayState.Playing, _provider.GetPlayState());
            Assert.Equal(1, _provider.PlayCallCount);
        }

        [Fact]
        public void Pause_ChangesStateToPaused()
        {
            _provider.CurrentPlayState = PlayState.Playing;
            _provider.Pause();

            Assert.Equal(PlayState.Paused, _provider.GetPlayState());
            Assert.Equal(1, _provider.PauseCallCount);
        }

        [Fact]
        public void PlayPause_TogglesState()
        {
            _provider.CurrentPlayState = PlayState.Stopped;

            _provider.PlayPause();
            Assert.Equal(PlayState.Playing, _provider.GetPlayState());

            _provider.PlayPause();
            Assert.Equal(PlayState.Paused, _provider.GetPlayState());
        }

        [Fact]
        public void Stop_ChangesStateToStopped()
        {
            _provider.CurrentPlayState = PlayState.Playing;
            _provider.StopPlayback();

            Assert.Equal(PlayState.Stopped, _provider.GetPlayState());
            Assert.Equal(1, _provider.StopCallCount);
        }

        [Fact]
        public void PlayNext_IncreasesCallCount()
        {
            _provider.PlayNext();
            _provider.PlayNext();

            Assert.Equal(2, _provider.PlayNextCallCount);
        }

        [Fact]
        public void PlayPrevious_IncreasesCallCount()
        {
            _provider.PlayPrevious();

            Assert.Equal(1, _provider.PlayPreviousCallCount);
        }

        #endregion

        #region Settings Tests

        [Theory]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(100)]
        public void SetVolume_ValidRange_ReturnsTrue(int volume)
        {
            Assert.True(_provider.SetVolume(volume));
            Assert.Equal(volume, _provider.GetVolume());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        public void SetVolume_InvalidRange_ReturnsFalse(int volume)
        {
            var originalVolume = _provider.GetVolume();
            Assert.False(_provider.SetVolume(volume));
            Assert.Equal(originalVolume, _provider.GetVolume());
        }

        [Fact]
        public void SetVolume_UnmutesMutedPlayer()
        {
            _provider.IsMuted = true;
            _provider.SetVolume(50);

            Assert.False(_provider.GetMute());
        }

        [Fact]
        public void SetMute_SetsState()
        {
            _provider.SetMute(true);
            Assert.True(_provider.GetMute());

            _provider.SetMute(false);
            Assert.False(_provider.GetMute());
        }

        [Theory]
        [InlineData(RepeatMode.None)]
        [InlineData(RepeatMode.All)]
        [InlineData(RepeatMode.One)]
        public void SetRepeatMode_SetsCorrectMode(RepeatMode mode)
        {
            _provider.SetRepeatMode(mode);
            Assert.Equal(mode, _provider.GetRepeatMode());
        }

        [Fact]
        public void SetShuffle_SetsState()
        {
            _provider.SetShuffle(true);
            Assert.True(_provider.GetShuffle());

            _provider.SetShuffle(false);
            Assert.False(_provider.GetShuffle());
        }

        [Fact]
        public void SetAutoDj_SetsState()
        {
            _provider.SetAutoDj(true);
            Assert.True(_provider.GetAutoDjEnabled());

            _provider.SetAutoDj(false);
            Assert.False(_provider.GetAutoDjEnabled());
        }

        [Fact]
        public void SetScrobble_SetsState()
        {
            _provider.SetScrobble(true);
            Assert.True(_provider.GetScrobbleEnabled());

            _provider.SetScrobble(false);
            Assert.False(_provider.GetScrobbleEnabled());
        }

        [Fact]
        public void SetPosition_SetsPosition()
        {
            _provider.SetPosition(60000);
            Assert.Equal(60000, _provider.GetPosition());
        }

        #endregion

        #region Composite Status Tests

        [Fact]
        public void GetPlayerStatus_ReturnsCorrectStatus()
        {
            _provider.CurrentPlayState = PlayState.Playing;
            _provider.CurrentVolume = 75;
            _provider.IsMuted = false;
            _provider.CurrentRepeatMode = RepeatMode.All;
            _provider.ShuffleEnabled = true;
            _provider.ScrobblingEnabled = true;

            var status = _provider.GetPlayerStatus(false);

            Assert.Equal("Playing", status.State);
            Assert.Equal("75", status.Volume);
            Assert.False(status.Mute);
            Assert.Equal("All", status.Repeat);
            Assert.True(status.Scrobble);
        }

        [Fact]
        public void GetPlayerStatus_LegacyFormat_ReturnsBoolShuffle()
        {
            _provider.ShuffleEnabled = true;
            _provider.AutoDjEnabled = false;

            var status = _provider.GetPlayerStatus(legacyShuffleFormat: true);

            Assert.IsType<bool>(status.Shuffle);
            Assert.True((bool)status.Shuffle);
        }

        [Fact]
        public void GetPlayerStatus_ModernFormat_ReturnsShuffleState()
        {
            _provider.ShuffleEnabled = true;
            _provider.AutoDjEnabled = false;

            var status = _provider.GetPlayerStatus(legacyShuffleFormat: false);

            Assert.Equal("Shuffle", status.Shuffle.ToString());
        }

        #endregion

        #region Output Device Tests

        [Fact]
        public void GetOutputDevices_ReturnsDevices()
        {
            var devices = _provider.GetOutputDevices();

            Assert.NotNull(devices);
            Assert.NotEmpty(devices.DeviceNames);
            Assert.NotEmpty(devices.ActiveDeviceName);
        }

        [Fact]
        public void SetOutputDevice_ChangesActiveDevice()
        {
            _provider.SetOutputDevice("Headphones");
            Assert.Equal("Headphones", _provider.GetOutputDevices().ActiveDeviceName);
        }

        #endregion
    }
}
