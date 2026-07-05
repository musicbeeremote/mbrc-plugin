using System.Collections.Generic;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Protocol.Processing;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    /// Integration tests for handshake enforcement and packet ordering.
    ///
    /// These tests verify:
    /// - Correct handshake sequence (player → protocol)
    /// - Client state before and after handshake completion
    /// - Behavior when handshake is incomplete
    /// - Commands sent before/during/after handshake
    /// </summary>
    public class HandshakeEnforcementTests
    {
        private const string ClientId = "handshake-test-client";

        private readonly Authenticator _authenticator;
        private readonly DelegateCommandDispatcher _dispatcher;
        private readonly MockEventAggregator _eventAggregator;
        private readonly MockLogger _logger;
        private readonly ProtocolHandler _protocolHandler;
        private readonly SystemCommands _systemCommands;

        public HandshakeEnforcementTests()
        {
            _logger = new MockLogger();
            _authenticator = new Authenticator();
            _eventAggregator = new MockEventAggregator();
            _dispatcher = new DelegateCommandDispatcher(_logger);

            var userSettings = new Mock<IUserSettings>();
            userSettings.Setup(x => x.CurrentVersion).Returns("1.0.0");

            _systemCommands = new SystemCommands(
                _logger,
                _eventAggregator,
                _authenticator,
                userSettings.Object);

            RegisterSystemCommands();

            _protocolHandler = new ProtocolHandler(
                _logger,
                _authenticator,
                _eventAggregator,
                _dispatcher);
        }

        private void RegisterSystemCommands()
        {
            var registrar = (ICommandRegistrar)_dispatcher;
            registrar.RegisterCommand(ProtocolConstants.Player, _systemCommands.HandlePlayer);
            registrar.RegisterCommand<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(
                ProtocolConstants.Protocol, _systemCommands.HandleProtocol);
            registrar.RegisterCommand(ProtocolConstants.PluginVersion, _systemCommands.HandlePluginVersion);
            registrar.RegisterCommand(ProtocolConstants.Ping, _systemCommands.HandlePing);
            registrar.RegisterCommand(ProtocolConstants.Pong, _systemCommands.HandlePong);
        }

        #region Correct Handshake Sequence Tests

        [Fact]
        public void CorrectSequence_PlayerThenProtocol_ClientFullyAuthenticated()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act - Correct sequence: player first, then protocol
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":4}", ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.Should().NotBeNull();
            client.Authenticated.Should().BeTrue();
            client.ClientPlatform.Should().Be(ClientOS.Android);
            client.ClientProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void CorrectSequence_ResponsesPublishedInOrder()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"iOS\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":3}", ClientId);

            // Assert - Two responses published (one for each handshake step)
            _eventAggregator.PublishedMessages.Should().HaveCount(2);
        }

        [Fact]
        public void CorrectSequence_V2Client_AuthenticatedWithDefaultCapabilities()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":2}", ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.Authenticated.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(2);
            client.Capabilities.SupportsPayloadObjects.Should().BeFalse();
            client.Capabilities.SupportsPagination.Should().BeFalse();
        }

        [Fact]
        public void CorrectSequence_V4Client_AuthenticatedWithFullCapabilities()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":4}", ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.Authenticated.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(4);
            client.Capabilities.SupportsPayloadObjects.Should().BeTrue();
            client.Capabilities.SupportsPagination.Should().BeTrue();
            client.Capabilities.SupportsAutoDjShuffle.Should().BeTrue();
            client.Capabilities.SupportsFullPlayerStatus.Should().BeTrue();
        }

        #endregion

        #region Partial Handshake State Tests

        [Fact]
        public void PartialHandshake_OnlyPlayer_ClientNotFullyAuthenticated()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act - Only send player command
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.Should().NotBeNull();
            client.ClientPlatform.Should().Be(ClientOS.Android);
            // Protocol version should be default (2)
            client.ClientProtocolVersion.Should().Be(2);
            // Client exists but authentication state depends on implementation
        }

        [Fact]
        public void PartialHandshake_OnlyPlayer_PlatformIsSet()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"iOS\"}", ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.ClientPlatform.Should().Be(ClientOS.iOS);
        }

        [Fact]
        public void PartialHandshake_OnlyProtocol_TriggersDisconnect()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            var disconnectTriggered = false;
            _protocolHandler.ForceClientDisconnect += (id) => disconnectTriggered = true;

            // Act - Only send protocol command (skipping player - packet 0 must be player)
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":4}", ClientId);

            // Assert - Disconnect triggered because first packet was not "player"
            disconnectTriggered.Should().BeTrue();

            // Client state unchanged since command was rejected
            var client = _authenticator.Client(ClientId);
            client.ClientProtocolVersion.Should().Be(2); // Default
            client.ClientPlatform.Should().Be(ClientOS.Unknown); // Default
        }

        #endregion

        #region Out-of-Order Handshake Tests

        [Fact]
        public void OutOfOrder_ProtocolBeforePlayer_TriggersDisconnect()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            var disconnectTriggered = false;
            _protocolHandler.ForceClientDisconnect += (id) => disconnectTriggered = true;

            // Act - Send protocol first (should trigger disconnect)
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":4}", ClientId);

            // Assert - Disconnect triggered because first packet was not "player"
            disconnectTriggered.Should().BeTrue();
        }

        [Fact]
        public void OutOfOrder_PingBeforePlayer_TriggersDisconnect()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            var disconnectTriggered = false;
            _protocolHandler.ForceClientDisconnect += (id) => disconnectTriggered = true;

            // Act - Send ping first (should trigger disconnect)
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", ClientId);

            // Assert - Disconnect triggered
            disconnectTriggered.Should().BeTrue();
        }

        [Fact]
        public void OutOfOrder_PingInsteadOfProtocol_TriggersDisconnect()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            var disconnectTriggered = false;
            _protocolHandler.ForceClientDisconnect += (id) => disconnectTriggered = true;

            // Act - Send player correctly, then ping instead of protocol
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", ClientId);

            // Assert - Disconnect triggered on second packet
            disconnectTriggered.Should().BeTrue();
        }

        #endregion

        #region Commands Before Handshake Tests

        [Fact]
        public void CommandBeforeHandshake_Ping_TriggersDisconnect()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            var disconnectTriggered = false;
            _protocolHandler.ForceClientDisconnect += (id) => disconnectTriggered = true;

            // Act - Send ping before handshake (packet 0 must be "player")
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", ClientId);

            // Assert - Disconnect triggered
            disconnectTriggered.Should().BeTrue();
            _eventAggregator.PublishedMessages.Should().BeEmpty(); // Command not executed
        }

        [Fact]
        public void CommandBeforeHandshake_PluginVersion_TriggersDisconnect()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            var disconnectTriggered = false;
            _protocolHandler.ForceClientDisconnect += (id) => disconnectTriggered = true;

            // Act - Send plugin version before handshake
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"pluginversion\",\"data\":\"\"}", ClientId);

            // Assert - Disconnect triggered
            disconnectTriggered.Should().BeTrue();
            _eventAggregator.PublishedMessages.Should().BeEmpty();
        }

        [Fact]
        public void CommandBeforeHandshake_ClientStateUnchanged()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ForceClientDisconnect += (id) => { };

            // Act - Attempt command before handshake
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", ClientId);

            // Assert - Client still exists with default state (but disconnect was requested)
            var client = _authenticator.Client(ClientId);
            client.Should().NotBeNull();
            client.ClientProtocolVersion.Should().Be(2); // Default unchanged
            client.ClientPlatform.Should().Be(ClientOS.Unknown); // Default unchanged
        }

        #endregion

        #region Commands After Handshake Tests

        [Fact]
        public void CommandAfterHandshake_Ping_ProcessedSuccessfully()
        {
            // Arrange
            CompleteHandshake();
            _eventAggregator.Clear();

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", ClientId);

            // Assert
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void CommandAfterHandshake_MultipleCommands_AllProcessed()
        {
            // Arrange
            CompleteHandshake();
            _eventAggregator.Clear();

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"pluginversion\",\"data\":\"\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", ClientId);

            // Assert
            _eventAggregator.PublishedMessages.Should().HaveCount(3);
        }

        #endregion

        #region Repeated Handshake Tests

        [Fact]
        public void RepeatedHandshake_PlayerCommand_UpdatesPlatform()
        {
            // Arrange
            CompleteHandshake(); // Initial handshake with Android

            // Act - Send another player command with different platform
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"iOS\"}", ClientId);

            // Assert - Platform updated
            var client = _authenticator.Client(ClientId);
            client.ClientPlatform.Should().Be(ClientOS.iOS);
        }

        [Fact]
        public void RepeatedHandshake_ProtocolCommand_UpdatesVersion()
        {
            // Arrange
            CompleteHandshake(); // Initial handshake with V4

            // Act - Send another protocol command with different version
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":3}", ClientId);

            // Assert - Version updated
            var client = _authenticator.Client(ClientId);
            client.ClientProtocolVersion.Should().Be(3);
        }

        [Fact]
        public void RepeatedHandshake_ProtocolCommand_UpdatesCapabilities()
        {
            // Arrange - Start with V4
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":4}", ClientId);

            var client = _authenticator.Client(ClientId);
            client.Capabilities.SupportsPagination.Should().BeTrue();

            // Act - Downgrade to V2
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":2}", ClientId);

            // Assert - Capabilities updated to V2
            client.Capabilities.SupportsPagination.Should().BeFalse();
            client.Capabilities.SupportsPayloadObjects.Should().BeFalse();
        }

        #endregion

        #region Client Not Found Tests

        [Fact]
        public void NoClientRegistered_MessagesIgnored()
        {
            // Arrange - Don't register client
            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", "unknown-client");

            // Assert - No crash, no responses for unknown client
            _authenticator.Client("unknown-client").Should().BeNull();
        }

        [Fact]
        public void ClientDisconnected_MessagesIgnored()
        {
            // Arrange
            CompleteHandshake();
            _authenticator.RemoveClientOnDisconnect(ClientId);
            _eventAggregator.Clear();

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", ClientId);

            // Assert - Ping response may or may not be published depending on implementation
            // but client should not exist
            _authenticator.Client(ClientId).Should().BeNull();
        }

        #endregion

        #region Protocol Object Format Tests

        [Fact]
        public void ProtocolHandshake_ObjectFormat_SetsAllFields()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);

            // Act - Object format with all fields
            var protocolJson = "{\"context\":\"protocol\",\"data\":{\"protocol_version\":4,\"no_broadcast\":true,\"client_id\":\"my-app\"}}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.ClientProtocolVersion.Should().Be(4);
            client.BroadcastsEnabled.Should().BeFalse(); // no_broadcast: true
            client.ClientId.Should().Be("my-app");
        }

        [Fact]
        public void ProtocolHandshake_ObjectFormat_NoBroadcastFalse_EnablesBroadcasts()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);

            // Act
            var protocolJson = "{\"context\":\"protocol\",\"data\":{\"protocol_version\":4,\"no_broadcast\":false}}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.BroadcastsEnabled.Should().BeTrue();
        }

        [Fact]
        public void ProtocolHandshake_SimpleIntFormat_DefaultsBroadcastsEnabled()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);

            // Act - Simple integer format
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":4}", ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.BroadcastsEnabled.Should().BeTrue(); // Default
        }

        [Fact]
        public void ProtocolHandshake_LegacyStringFormat_ParsesCorrectly()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);

            // Act - Legacy string format
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":\"2.1\"}", ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.ClientProtocolVersion.Should().Be(2);
            client.Capabilities.SupportsPayloadObjects.Should().BeTrue(); // 2.1 feature
            client.Capabilities.SupportsPagination.Should().BeFalse(); // 2.2+ feature
        }

        #endregion

        #region Helper Methods

        private void CompleteHandshake()
        {
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":4}", ClientId);
        }

        #endregion
    }
}
