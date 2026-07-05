using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Fixtures;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class PlayerCommandsTests
    {
        private const string TestConnectionId = "test-connection-123";

        private readonly Mock<IPlayerDataProvider> _playerDataProvider;
        private readonly MockLogger _logger;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly Mock<IProtocolCapabilities> _protocolCapabilities;
        private readonly PlayerCommands _sut;

        public PlayerCommandsTests()
        {
            _playerDataProvider = new Mock<IPlayerDataProvider>();
            _logger = new MockLogger();
            _eventAggregator = new Mock<IEventAggregator>();
            _protocolCapabilities = new Mock<IProtocolCapabilities>();

            _sut = new PlayerCommands(
                _playerDataProvider.Object,
                _logger,
                _eventAggregator.Object,
                _protocolCapabilities.Object);
        }

        #region 1.1 Simple Playback Controls

        [Fact]
        public void HandlePlay_Success_ReturnsTrue()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.Play()).Returns(true);
            var context = new TestCommandContext("play", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlay(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.Play(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlay_AdapterReturnsFalse_StillReturnsTrue()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.Play()).Returns(false);
            var context = new TestCommandContext("play", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlay(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.Play(), Times.Once);
        }

        [Fact]
        public void HandlePause_Success_ReturnsTrue()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.Pause()).Returns(true);
            var context = new TestCommandContext("pause", null, TestConnectionId);

            // Act
            var result = _sut.HandlePause(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.Pause(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlayPause_Success_ReturnsTrue()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.PlayPause()).Returns(true);
            var context = new TestCommandContext("playpause", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlayPause(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.PlayPause(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleStop_Success_ReturnsTrue()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.StopPlayback()).Returns(true);
            var context = new TestCommandContext("stop", null, TestConnectionId);

            // Act
            var result = _sut.HandleStop(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.StopPlayback(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleNext_Success_ReturnsTrue()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.PlayNext()).Returns(true);
            var context = new TestCommandContext("next", null, TestConnectionId);

            // Act
            var result = _sut.HandleNext(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.PlayNext(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePrevious_Success_ReturnsTrue()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.PlayPrevious()).Returns(true);
            var context = new TestCommandContext("previous", null, TestConnectionId);

            // Act
            var result = _sut.HandlePrevious(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.PlayPrevious(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 1.2 Volume Control

        [Fact]
        public void HandleVolumeSet_ValidVolume_SetsAndPublishes()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetVolume(75)).Returns(true);
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(75);
            var context = new TestCommandContext("volume", 75, TestConnectionId);

            // Act
            var result = _sut.HandleVolumeSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetVolume(75), Times.Once);
            _playerDataProvider.Verify(x => x.GetVolume(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleVolumeSet_NoData_ReturnsCurrentVolume()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(50);
            var context = new TestCommandContext("volume", null, TestConnectionId);

            // Act
            var result = _sut.HandleVolumeSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetVolume(It.IsAny<int>()), Times.Never);
            _playerDataProvider.Verify(x => x.GetVolume(), Times.Once);
        }

        [Fact]
        public void HandleVolumeSet_BoundaryZero_SetsVolume()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetVolume(0)).Returns(true);
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(0);
            var context = new TestCommandContext("volume", 0, TestConnectionId);

            // Act
            var result = _sut.HandleVolumeSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetVolume(0), Times.Once);
        }

        [Fact]
        public void HandleVolumeSet_BoundaryMax_SetsVolume()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetVolume(100)).Returns(true);
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(100);
            var context = new TestCommandContext("volume", 100, TestConnectionId);

            // Act
            var result = _sut.HandleVolumeSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetVolume(100), Times.Once);
        }

        [Fact]
        public void HandleVolumeSet_AdapterFails_PublishesCurrentVolume()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetVolume(75)).Returns(false);
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(50);
            var context = new TestCommandContext("volume", 75, TestConnectionId);

            // Act
            var result = _sut.HandleVolumeSet(context);

            // Assert
            result.Should().BeFalse();
            _playerDataProvider.Verify(x => x.GetVolume(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 1.3 Mute Control

        [Fact]
        public void HandleMuteSet_ToggleFromMuted_Unmutes()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetMute()).Returns(true);
            _playerDataProvider.Setup(x => x.SetMute(false)).Returns(true);
            var context = new TestCommandContext("mute", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleMuteSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetMute(false), Times.Once);
        }

        [Fact]
        public void HandleMuteSet_ToggleFromUnmuted_Mutes()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetMute()).Returns(false);
            _playerDataProvider.Setup(x => x.SetMute(true)).Returns(true);
            var context = new TestCommandContext("mute", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleMuteSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetMute(true), Times.Once);
        }

        [Fact]
        public void HandleMuteSet_ExplicitTrue_Mutes()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetMute(true)).Returns(true);
            _playerDataProvider.Setup(x => x.GetMute()).Returns(true);
            var context = new TestCommandContext("mute", true, TestConnectionId);

            // Act
            var result = _sut.HandleMuteSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetMute(true), Times.Once);
        }

        [Fact]
        public void HandleMuteSet_ExplicitFalse_Unmutes()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetMute(false)).Returns(true);
            _playerDataProvider.Setup(x => x.GetMute()).Returns(false);
            var context = new TestCommandContext("mute", false, TestConnectionId);

            // Act
            var result = _sut.HandleMuteSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetMute(false), Times.Once);
        }

        [Fact]
        public void HandleMuteSet_NoData_ReturnsCurrentState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetMute()).Returns(true);
            var context = new TestCommandContext("mute", null, TestConnectionId);

            // Act
            var result = _sut.HandleMuteSet(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetMute(It.IsAny<bool>()), Times.Never);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleMuteSet_AdapterFails_PublishesCurrentState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetMute(true)).Returns(false);
            _playerDataProvider.Setup(x => x.GetMute()).Returns(false);
            var context = new TestCommandContext("mute", true, TestConnectionId);

            // Act
            var result = _sut.HandleMuteSet(context);

            // Assert
            result.Should().BeFalse();
            _playerDataProvider.Verify(x => x.GetMute(), Times.AtLeastOnce);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 1.4 Shuffle Control - CRITICAL

        [Fact]
        public void HandleShuffle_LegacyClient_UsesSimpleShuffle()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(false);
            _playerDataProvider.Setup(x => x.GetShuffle()).Returns(false);
            _playerDataProvider.Setup(x => x.SetShuffle(true)).Returns(true);
            var context = new TestCommandContext("shuffle", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetShuffle(true), Times.Once);
        }

        [Fact]
        public void HandleShuffle_ModernClient_UsesAutoDjShuffle()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetShuffle()).Returns(false);
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.SetShuffle(true)).Returns(true);
            var context = new TestCommandContext("shuffle", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            // Modern client toggle starts with enabling shuffle
            _playerDataProvider.Verify(x => x.SetShuffle(true), Times.Once);
        }

        [Fact]
        public void HandleShuffle_SimpleToggleOn_EnablesShuffle()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(false);
            _playerDataProvider.Setup(x => x.GetShuffle()).Returns(false);
            _playerDataProvider.Setup(x => x.SetShuffle(true)).Returns(true);
            var context = new TestCommandContext("shuffle", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetShuffle(true), Times.Once);
        }

        [Fact]
        public void HandleShuffle_SimpleToggleOff_DisablesShuffle()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(false);
            _playerDataProvider.Setup(x => x.GetShuffle()).Returns(true);
            _playerDataProvider.Setup(x => x.SetShuffle(false)).Returns(true);
            var context = new TestCommandContext("shuffle", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetShuffle(false), Times.Once);
        }

        [Fact]
        public void HandleShuffle_ExplicitBoolTrue_SetsShuffle()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(false);
            _playerDataProvider.Setup(x => x.SetShuffle(true)).Returns(true);
            var context = new TestCommandContext("shuffle", true, TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetShuffle(true), Times.Once);
        }

        [Fact]
        public void HandleShuffle_ExplicitBoolFalse_DisablesShuffle()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(false);
            _playerDataProvider.Setup(x => x.SetShuffle(false)).Returns(true);
            var context = new TestCommandContext("shuffle", false, TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetShuffle(false), Times.Once);
        }

        [Fact]
        public void HandleShuffle_ExplicitStateOff_SetsBothOff()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.SetShuffle(false)).Returns(true);
            _playerDataProvider.Setup(x => x.SetAutoDj(false)).Returns(true);
            var context = new TestCommandContext("shuffle", ShuffleState.Off, TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetShuffle(false), Times.Once);
            _playerDataProvider.Verify(x => x.SetAutoDj(false), Times.Once);
        }

        [Fact]
        public void HandleShuffle_ExplicitStateShuffle_SetsShuffleOnly()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.SetAutoDj(false)).Returns(true);
            _playerDataProvider.Setup(x => x.SetShuffle(true)).Returns(true);
            var context = new TestCommandContext("shuffle", ShuffleState.Shuffle, TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetAutoDj(false), Times.Once);
            _playerDataProvider.Verify(x => x.SetShuffle(true), Times.Once);
        }

        [Fact]
        public void HandleShuffle_ExplicitStateAutoDj_SetsAutoDj()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.SetShuffle(true)).Returns(true);
            _playerDataProvider.Setup(x => x.SetAutoDj(true)).Returns(true);
            var context = new TestCommandContext("shuffle", ShuffleState.AutoDj, TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetShuffle(true), Times.Once);
            _playerDataProvider.Verify(x => x.SetAutoDj(true), Times.Once);
        }

        [Fact]
        public void HandleShuffle_AutoDjToggle_ShuffleOnAutoDjOff_EnablesAutoDj()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetShuffle()).Returns(true);
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.SetAutoDj(true)).Returns(true);
            var context = new TestCommandContext("shuffle", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetAutoDj(true), Times.Once);
        }

        [Fact]
        public void HandleShuffle_AutoDjToggle_AutoDjOn_DisablesAutoDj()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetShuffle()).Returns(true);
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(true);
            _playerDataProvider.Setup(x => x.SetAutoDj(false)).Returns(true);
            var context = new TestCommandContext("shuffle", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetAutoDj(false), Times.Once);
        }

        [Fact]
        public void HandleShuffle_AutoDjToggle_BothOff_EnablesShuffle()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetShuffle()).Returns(false);
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.SetShuffle(true)).Returns(true);
            var context = new TestCommandContext("shuffle", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetShuffle(true), Times.Once);
        }

        [Fact]
        public void HandleShuffle_NoData_ReturnsCurrentState()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsAutoDjShuffle(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetShuffleState()).Returns(ShuffleState.Shuffle);
            var context = new TestCommandContext("shuffle", null, TestConnectionId);

            // Act
            var result = _sut.HandleShuffle(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.GetShuffleState(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 1.5 Repeat Control

        [Fact]
        public void HandleRepeat_ToggleFromNone_SetsAll()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetRepeatMode()).Returns(RepeatMode.None);
            _playerDataProvider.Setup(x => x.SetRepeatMode(RepeatMode.All)).Returns(true);
            var context = new TestCommandContext("repeat", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleRepeat(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetRepeatMode(RepeatMode.All), Times.Once);
        }

        [Fact]
        public void HandleRepeat_ToggleFromAll_SetsOne()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetRepeatMode()).Returns(RepeatMode.All);
            _playerDataProvider.Setup(x => x.SetRepeatMode(RepeatMode.One)).Returns(true);
            var context = new TestCommandContext("repeat", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleRepeat(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetRepeatMode(RepeatMode.One), Times.Once);
        }

        [Fact]
        public void HandleRepeat_ToggleFromOne_SetsNone()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetRepeatMode()).Returns(RepeatMode.One);
            _playerDataProvider.Setup(x => x.SetRepeatMode(RepeatMode.None)).Returns(true);
            var context = new TestCommandContext("repeat", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleRepeat(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetRepeatMode(RepeatMode.None), Times.Once);
        }

        [Fact]
        public void HandleRepeat_ExplicitMode_SetsMode()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetRepeatMode(RepeatMode.One)).Returns(true);
            var context = new TestCommandContext("repeat", RepeatMode.One, TestConnectionId);

            // Act
            var result = _sut.HandleRepeat(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetRepeatMode(RepeatMode.One), Times.Once);
        }

        [Fact]
        public void HandleRepeat_NoData_ReturnsCurrentState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetRepeatMode()).Returns(RepeatMode.All);
            var context = new TestCommandContext("repeat", null, TestConnectionId);

            // Act
            var result = _sut.HandleRepeat(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.GetRepeatMode(), Times.Once);
            _playerDataProvider.Verify(x => x.SetRepeatMode(It.IsAny<RepeatMode>()), Times.Never);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Theory]
        [InlineData(RepeatMode.None)]
        [InlineData(RepeatMode.All)]
        [InlineData(RepeatMode.One)]
        public void HandleRepeat_AllModes_SetsCorrectly(RepeatMode mode)
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetRepeatMode(mode)).Returns(true);
            var context = new TestCommandContext("repeat", mode, TestConnectionId);

            // Act
            var result = _sut.HandleRepeat(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetRepeatMode(mode), Times.Once);
        }

        #endregion

        #region 1.6 Scrobble and AutoDJ

        [Fact]
        public void HandleScrobble_Toggle_TogglesState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetScrobbleEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.SetScrobble(true)).Returns(true);
            var context = new TestCommandContext("scrobble", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleScrobble(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetScrobble(true), Times.Once);
        }

        [Fact]
        public void HandleScrobble_ExplicitSet_SetsState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetScrobble(true)).Returns(true);
            _playerDataProvider.Setup(x => x.GetScrobbleEnabled()).Returns(true);
            var context = new TestCommandContext("scrobble", true, TestConnectionId);

            // Act
            var result = _sut.HandleScrobble(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetScrobble(true), Times.Once);
        }

        [Fact]
        public void HandleAutoDj_Toggle_TogglesState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.SetAutoDj(true)).Returns(true);
            var context = new TestCommandContext("autodj", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleAutoDj(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetAutoDj(true), Times.Once);
        }

        [Fact]
        public void HandleAutoDj_ExplicitSet_SetsState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.SetAutoDj(true)).Returns(true);
            var context = new TestCommandContext("autodj", true, TestConnectionId);

            // Act
            var result = _sut.HandleAutoDj(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetAutoDj(true), Times.Once);
        }

        [Fact]
        public void HandleAutoDj_NoData_ReturnsCurrentState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(true);
            var context = new TestCommandContext("autodj", null, TestConnectionId);

            // Act
            var result = _sut.HandleAutoDj(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.GetAutoDjEnabled(), Times.Once);
            _playerDataProvider.Verify(x => x.SetAutoDj(It.IsAny<bool>()), Times.Never);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 1.7 Player Status

        [Fact]
        public void HandlePlayerStatus_LegacyClient_ReturnsBoolShuffle()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsFullPlayerStatus(TestConnectionId)).Returns(false);
            _playerDataProvider.Setup(x => x.GetPlayerStatus(true))
                .Returns(new PlayerStatus { State = "Playing", Volume = "50" });
            var context = new TestCommandContext("playerstatus", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlayerStatus(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.GetPlayerStatus(true), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlayerStatus_ModernClient_ReturnsFullStatus()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsFullPlayerStatus(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetPlayerStatus(false))
                .Returns(new PlayerStatus { State = "Playing", Volume = "50", Shuffle = ShuffleState.Shuffle });
            var context = new TestCommandContext("playerstatus", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlayerStatus(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.GetPlayerStatus(false), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 1.8 Output Devices

        [Fact]
        public void HandleOutputDevices_ReturnsDeviceList()
        {
            // Arrange
            var devices = new OutputDevice(new[] { "Default", "Speakers" }, "Default");
            _playerDataProvider.Setup(x => x.GetOutputDevices()).Returns(devices);
            var context = new TestCommandContext("outputdevices", null, TestConnectionId);

            // Act
            var result = _sut.HandleOutputDevices(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.GetOutputDevices(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleOutputDeviceSwitch_ValidDevice_Switches()
        {
            // Arrange
            var devices = new OutputDevice(new[] { "Default", "Speakers" }, "Speakers");
            _playerDataProvider.Setup(x => x.SetOutputDevice("Speakers")).Returns(true);
            _playerDataProvider.Setup(x => x.GetOutputDevices()).Returns(devices);
            var context = new TestCommandContext("outputdeviceswitch", "Speakers", TestConnectionId);

            // Act
            var result = _sut.HandleOutputDeviceSwitch(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.SetOutputDevice("Speakers"), Times.Once);
            _playerDataProvider.Verify(x => x.GetOutputDevices(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleOutputDeviceSwitch_EmptyDeviceName_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("outputdeviceswitch", string.Empty, TestConnectionId);

            // Act
            var result = _sut.HandleOutputDeviceSwitch(context);

            // Assert
            result.Should().BeFalse();
            _playerDataProvider.Verify(x => x.SetOutputDevice(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandleOutputDeviceSwitch_NullDeviceName_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("outputdeviceswitch", null, TestConnectionId);

            // Act
            var result = _sut.HandleOutputDeviceSwitch(context);

            // Assert
            result.Should().BeFalse();
            _playerDataProvider.Verify(x => x.SetOutputDevice(It.IsAny<string>()), Times.Never);
        }

        #endregion
    }
}
