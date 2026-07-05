using System.Linq;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Protocol.Processing;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    /// Integration tests for player control commands.
    /// Tests the full message flow: JSON → ProtocolHandler → CommandDispatcher → PlayerCommands → Response
    /// </summary>
    public class PlayerCommandIntegrationTests
    {
        private const string ClientId = "player-test-client";

        private readonly Authenticator _authenticator;
        private readonly DelegateCommandDispatcher _dispatcher;
        private readonly MockEventAggregator _eventAggregator;
        private readonly MockLogger _logger;
        private readonly Mock<IPlayerDataProvider> _playerDataProvider;
        private readonly ProtocolCapabilities _capabilities;
        private readonly PlayerCommands _playerCommands;
        private readonly ProtocolHandler _protocolHandler;

        public PlayerCommandIntegrationTests()
        {
            _logger = new MockLogger();
            _authenticator = new Authenticator();
            _eventAggregator = new MockEventAggregator();
            _dispatcher = new DelegateCommandDispatcher(_logger);
            _playerDataProvider = new Mock<IPlayerDataProvider>();
            _capabilities = new ProtocolCapabilities(_authenticator);

            _playerCommands = new PlayerCommands(
                _playerDataProvider.Object,
                _logger,
                _eventAggregator,
                _capabilities);

            RegisterCommands();

            _protocolHandler = new ProtocolHandler(
                _logger,
                _authenticator,
                _eventAggregator,
                _dispatcher);
        }

        private void RegisterCommands()
        {
            var registrar = (ICommandRegistrar)_dispatcher;

            // Register system commands for handshake
            var userSettings = new Mock<MusicBeePlugin.Services.Configuration.IUserSettings>();
            userSettings.Setup(x => x.CurrentVersion).Returns("1.0.0");
            var systemCommands = new SystemCommands(_logger, _eventAggregator, _authenticator, userSettings.Object);
            registrar.RegisterCommand(ProtocolConstants.Player, systemCommands.HandlePlayer);
            registrar.RegisterCommand<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(
                ProtocolConstants.Protocol, systemCommands.HandleProtocol);

            // Register player commands
            registrar.RegisterCommand(ProtocolConstants.PlayerPlay, _playerCommands.HandlePlay);
            registrar.RegisterCommand(ProtocolConstants.PlayerPause, _playerCommands.HandlePause);
            registrar.RegisterCommand(ProtocolConstants.PlayerPlayPause, _playerCommands.HandlePlayPause);
            registrar.RegisterCommand(ProtocolConstants.PlayerStop, _playerCommands.HandleStop);
            registrar.RegisterCommand(ProtocolConstants.PlayerNext, _playerCommands.HandleNext);
            registrar.RegisterCommand(ProtocolConstants.PlayerPrevious, _playerCommands.HandlePrevious);
            registrar.RegisterCommand(ProtocolConstants.PlayerVolume, _playerCommands.HandleVolumeSet);
            registrar.RegisterCommand(ProtocolConstants.PlayerMute, _playerCommands.HandleMuteSet);
            registrar.RegisterCommand(ProtocolConstants.PlayerShuffle, _playerCommands.HandleShuffle);
            registrar.RegisterCommand(ProtocolConstants.PlayerScrobble, _playerCommands.HandleScrobble);
            registrar.RegisterCommand(ProtocolConstants.PlayerAutoDj, _playerCommands.HandleAutoDj);
            registrar.RegisterCommand(ProtocolConstants.PlayerRepeat, _playerCommands.HandleRepeat);
            registrar.RegisterCommand(ProtocolConstants.PlayerStatus, _playerCommands.HandlePlayerStatus);
            registrar.RegisterCommand(ProtocolConstants.PlayerOutput, _playerCommands.HandleOutputDevices);
            registrar.RegisterCommand(ProtocolConstants.PlayerOutputSwitch, _playerCommands.HandleOutputDeviceSwitch);
        }

        private void CompleteHandshake(int protocolVersion = 4)
        {
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage($"{{\"context\":\"protocol\",\"data\":{protocolVersion}}}", ClientId);
            _eventAggregator.Clear();
        }

        #region Playback Control Tests

        [Fact]
        public void Play_AfterHandshake_ExecutesAndPublishesResponse()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.Play()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerplay\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.Play(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Pause_AfterHandshake_ExecutesAndPublishesResponse()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.Pause()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerpause\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.Pause(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void PlayPause_AfterHandshake_TogglesPlayback()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.PlayPause()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerplaypause\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.PlayPause(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Stop_AfterHandshake_StopsPlayback()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.StopPlayback()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerstop\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.StopPlayback(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Next_AfterHandshake_PlaysNextTrack()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.PlayNext()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playernext\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.PlayNext(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Previous_AfterHandshake_PlaysPreviousTrack()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.PlayPrevious()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerprevious\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.PlayPrevious(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        #endregion

        #region Volume Control Tests

        [Fact]
        public void Volume_SetValue_SetsVolumeAndReturnsCurrentVolume()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.SetVolume(75)).Returns(true);
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(75);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playervolume\",\"data\":75}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetVolume(75), Times.Once);
            _playerDataProvider.Verify(x => x.GetVolume(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Volume_NoData_ReturnsCurrentVolume()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(50);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playervolume\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetVolume(It.IsAny<int>()), Times.Never);
            _playerDataProvider.Verify(x => x.GetVolume(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        #endregion

        #region Mute Control Tests

        [Fact]
        public void Mute_SetTrue_MutesPlayer()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.SetMute(true)).Returns(true);
            _playerDataProvider.Setup(x => x.GetMute()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playermute\",\"data\":true}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetMute(true), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Mute_Toggle_TogglesCurrentState()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetMute()).Returns(false);
            _playerDataProvider.Setup(x => x.SetMute(true)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playermute\",\"data\":\"toggle\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.GetMute(), Times.AtLeastOnce);
            _playerDataProvider.Verify(x => x.SetMute(true), Times.Once);
        }

        #endregion

        #region Shuffle Control Tests

        [Fact]
        public void Shuffle_V2Client_UsesSimpleShuffle()
        {
            // Arrange
            CompleteHandshake(2);
            _playerDataProvider.Setup(x => x.GetShuffleState()).Returns(ShuffleState.Shuffle);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playershuffle\",\"data\":\"\"}", ClientId);

            // Assert - V2 doesn't support AutoDJ shuffle
            _playerDataProvider.Verify(x => x.GetShuffleState(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Shuffle_V4Client_SupportsAutoDjShuffle()
        {
            // Arrange
            CompleteHandshake(4);
            _playerDataProvider.Setup(x => x.GetShuffleState()).Returns(ShuffleState.AutoDj);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playershuffle\",\"data\":\"\"}", ClientId);

            // Assert - V4 supports AutoDJ shuffle
            _playerDataProvider.Verify(x => x.GetShuffleState(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Shuffle_Toggle_CyclesShuffleState()
        {
            // Arrange
            CompleteHandshake(4);
            _playerDataProvider.Setup(x => x.GetShuffle()).Returns(false);
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.SetShuffle(true)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playershuffle\",\"data\":\"toggle\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetShuffle(true), Times.Once);
        }

        #endregion

        #region Repeat Control Tests

        [Fact]
        public void Repeat_Toggle_CyclesRepeatMode()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetRepeatMode()).Returns(RepeatMode.None);
            _playerDataProvider.Setup(x => x.SetRepeatMode(RepeatMode.All)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerrepeat\",\"data\":\"toggle\"}", ClientId);

            // Assert - None -> All
            _playerDataProvider.Verify(x => x.SetRepeatMode(RepeatMode.All), Times.Once);
        }

        [Fact]
        public void Repeat_Query_ReturnsCurrentMode()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetRepeatMode()).Returns(RepeatMode.One);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerrepeat\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.GetRepeatMode(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        #endregion

        #region Scrobble Control Tests

        [Fact]
        public void Scrobble_Toggle_TogglesScrobbleState()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetScrobbleEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.SetScrobble(true)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"scrobbler\",\"data\":\"toggle\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetScrobble(true), Times.Once);
        }

        [Fact]
        public void Scrobble_SetValue_SetsScrobbleState()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.SetScrobble(true)).Returns(true);
            _playerDataProvider.Setup(x => x.GetScrobbleEnabled()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"scrobbler\",\"data\":true}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetScrobble(true), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        #endregion

        #region AutoDJ Control Tests

        [Fact]
        public void AutoDj_Toggle_TogglesAutoDjState()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.SetAutoDj(true)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerautodj\",\"data\":\"toggle\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetAutoDj(true), Times.Once);
        }

        [Fact]
        public void AutoDj_Query_ReturnsCurrentState()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetAutoDjEnabled()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerautodj\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.GetAutoDjEnabled(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        #endregion

        #region Player Status Tests

        [Fact]
        public void PlayerStatus_V4Client_ReturnsFullStatus()
        {
            // Arrange
            CompleteHandshake(4);
            var status = new PlayerStatus { Volume = "80", Mute = false, Repeat = "All" };
            _playerDataProvider.Setup(x => x.GetPlayerStatus(false)).Returns(status);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerstatus\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.GetPlayerStatus(false), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void PlayerStatus_V2Client_ReturnsLegacyStatus()
        {
            // Arrange
            CompleteHandshake(2);
            var status = new PlayerStatus { Volume = "50" };
            _playerDataProvider.Setup(x => x.GetPlayerStatus(true)).Returns(status);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerstatus\",\"data\":\"\"}", ClientId);

            // Assert - V2 client gets legacy status
            _playerDataProvider.Verify(x => x.GetPlayerStatus(true), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        #endregion

        #region Output Device Tests

        [Fact]
        public void OutputDevices_Query_ReturnsDeviceList()
        {
            // Arrange
            CompleteHandshake();
            var devices = new OutputDevice(new[] { "Speakers", "Headphones" }, "Speakers");
            _playerDataProvider.Setup(x => x.GetOutputDevices()).Returns(devices);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playeroutput\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.GetOutputDevices(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void OutputDeviceSwitch_ValidDevice_SwitchesAndReturnsUpdatedList()
        {
            // Arrange
            CompleteHandshake();
            var devices = new OutputDevice(new[] { "Speakers", "Headphones" }, "Headphones");
            _playerDataProvider.Setup(x => x.SetOutputDevice("Headphones"));
            _playerDataProvider.Setup(x => x.GetOutputDevices()).Returns(devices);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playeroutputswitch\",\"data\":\"Headphones\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetOutputDevice("Headphones"), Times.Once);
            _playerDataProvider.Verify(x => x.GetOutputDevices(), Times.Once);

            // Verify JSON response structure
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlayerOutput);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playeroutput");
            response["data"]["active"].ToString().Should().Be("Headphones");
            response["data"]["devices"].Should().HaveCount(2);
        }

        [Fact]
        public void OutputDeviceSwitch_EmptyDeviceName_DoesNotSwitch()
        {
            // Arrange
            CompleteHandshake();

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playeroutputswitch\",\"data\":\"\"}", ClientId);

            // Assert - Should not call SetOutputDevice with empty name
            _playerDataProvider.Verify(x => x.SetOutputDevice(It.IsAny<string>()), Times.Never);
            _playerDataProvider.Verify(x => x.GetOutputDevices(), Times.Never);
        }

        [Fact]
        public void OutputDeviceSwitch_NullData_DoesNotSwitch()
        {
            // Arrange
            CompleteHandshake();

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playeroutputswitch\",\"data\":null}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.SetOutputDevice(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region Multiple Commands Tests

        [Fact]
        public void MultipleCommands_InSequence_AllProcessedCorrectly()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.Play()).Returns(true);
            _playerDataProvider.Setup(x => x.PlayNext()).Returns(true);
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(75);

            // Act - Send multiple commands
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerplay\",\"data\":\"\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playernext\",\"data\":\"\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playervolume\",\"data\":\"\"}", ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.Play(), Times.Once);
            _playerDataProvider.Verify(x => x.PlayNext(), Times.Once);
            _playerDataProvider.Verify(x => x.GetVolume(), Times.Once);
            _eventAggregator.PublishedMessages.Should().HaveCount(3);
        }

        [Fact]
        public void MultipleCommands_InSinglePayload_AllProcessed()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.Play()).Returns(true);
            _playerDataProvider.Setup(x => x.PlayPause()).Returns(true);

            // Act - Send multiple commands in single payload (newline separated)
            _protocolHandler.ProcessIncomingMessage(
                "{\"context\":\"playerplay\",\"data\":\"\"}\r\n{\"context\":\"playerplaypause\",\"data\":\"\"}",
                ClientId);

            // Assert
            _playerDataProvider.Verify(x => x.Play(), Times.Once);
            _playerDataProvider.Verify(x => x.PlayPause(), Times.Once);
            _eventAggregator.PublishedMessages.Should().HaveCount(2);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void Command_BeforeHandshake_NotProcessed()
        {
            // Arrange - Don't complete handshake, just add client
            _authenticator.AddClientOnConnect(ClientId);
            var disconnectTriggered = false;
            _protocolHandler.ForceClientDisconnect += (id) => disconnectTriggered = true;

            // Act - Try to send player command before handshake
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerplay\",\"data\":\"\"}", ClientId);

            // Assert - Command rejected, disconnect triggered
            disconnectTriggered.Should().BeTrue();
            _playerDataProvider.Verify(x => x.Play(), Times.Never);
        }

        [Fact]
        public void UnknownCommand_DoesNotCrash()
        {
            // Arrange
            CompleteHandshake();

            // Act - Send unknown command
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"unknownplayercommand\",\"data\":\"\"}", ClientId);

            // Assert - No crash, no response for unknown command
            _eventAggregator.PublishedMessages.Should().BeEmpty();
        }

        #endregion

        #region Serialized Response Verification Tests

        [Fact]
        public void Volume_Query_ReturnsCorrectJsonResponse()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(75);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playervolume\",\"data\":\"\"}", ClientId);

            // Assert - Verify serialized response
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlayerVolume);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be(ProtocolConstants.PlayerVolume);
            response["data"].ToObject<int>().Should().Be(75);
        }

        [Fact]
        public void Mute_Query_ReturnsCorrectJsonStructure()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetMute()).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playermute\",\"data\":\"\"}", ClientId);

            // Assert - Verify JSON structure
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlayerMute);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be(ProtocolConstants.PlayerMute);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        [Fact]
        public void PlayerStatus_Query_ReturnsCorrectJsonPayload()
        {
            // Arrange
            CompleteHandshake();
            var status = new PlayerStatus
            {
                Volume = "85",
                Mute = false,
                Shuffle = "shuffle",
                Repeat = "All",
                Scrobble = true,
                State = "Playing"
            };
            _playerDataProvider.Setup(x => x.GetPlayerStatus(false)).Returns(status);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playerstatus\",\"data\":\"\"}", ClientId);

            // Assert - Verify serialized response data
            var responseData = _eventAggregator.GetFirstResponseData<PlayerStatus>(ProtocolConstants.PlayerStatus);
            responseData.Should().NotBeNull();
            responseData.Volume.Should().Be("85");
            responseData.Mute.Should().BeFalse();
            responseData.Shuffle.Should().Be("shuffle");
            responseData.Repeat.Should().Be("All");
            responseData.Scrobble.Should().BeTrue();
            responseData.State.Should().Be("Playing");
        }

        [Fact]
        public void OutputDevices_Query_ReturnsSerializedDeviceList()
        {
            // Arrange
            CompleteHandshake();
            var devices = new OutputDevice(new[] { "Speakers", "Headphones", "External DAC" }, "Headphones");
            _playerDataProvider.Setup(x => x.GetOutputDevices()).Returns(devices);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playeroutput\",\"data\":\"\"}", ClientId);

            // Assert - Verify serialized device list
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlayerOutput);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be(ProtocolConstants.PlayerOutput);

            var data = response["data"];
            data["active"].ToString().Should().Be("Headphones");
            data["devices"].Should().HaveCount(3);
        }

        [Fact]
        public void AllResponses_HaveCorrectConnectionId()
        {
            // Arrange
            CompleteHandshake();
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(50);
            _playerDataProvider.Setup(x => x.GetMute()).Returns(false);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playervolume\",\"data\":\"\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playermute\",\"data\":\"\"}", ClientId);

            // Assert - All responses should target the correct client
            var events = _eventAggregator.GetMessageSendEvents().ToList();
            events.Should().HaveCount(2);
            events.Should().OnlyContain(e => e.ConnectionId == ClientId);
        }

        #endregion
    }
}
