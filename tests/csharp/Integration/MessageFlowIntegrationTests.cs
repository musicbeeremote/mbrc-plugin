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
    /// Integration tests for the full message flow:
    /// Raw JSON → ProtocolHandler → CommandDispatcher → Handler → Response
    ///
    /// These tests verify the entire pipeline works end-to-end without mocking
    /// the command dispatcher, ensuring real command registration and execution.
    /// </summary>
    public class MessageFlowIntegrationTests
    {
        private const string ClientId = "integration-test-client";

        private readonly Authenticator _authenticator;
        private readonly DelegateCommandDispatcher _dispatcher;
        private readonly MockEventAggregator _eventAggregator;
        private readonly MockLogger _logger;
        private readonly ProtocolHandler _protocolHandler;
        private readonly SystemCommands _systemCommands;

        public MessageFlowIntegrationTests()
        {
            _logger = new MockLogger();
            _authenticator = new Authenticator();
            _eventAggregator = new MockEventAggregator();
            _dispatcher = new DelegateCommandDispatcher(_logger);

            var userSettings = new Mock<IUserSettings>();
            userSettings.Setup(x => x.CurrentVersion).Returns("1.0.0");

            // Create real command handlers
            _systemCommands = new SystemCommands(
                _logger,
                _eventAggregator,
                _authenticator,
                userSettings.Object);

            // Register commands with the dispatcher
            RegisterSystemCommands();

            // Create the protocol handler with real dependencies
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

        #region Full Handshake Flow Tests

        [Fact]
        public void FullHandshake_JsonToResponse_CompletesSuccessfully()
        {
            // Arrange - Connect client
            _authenticator.AddClientOnConnect(ClientId);

            // Act - Send player command (first packet)
            var playerJson = "{\"context\":\"player\",\"data\":\"Android\"}";
            _protocolHandler.ProcessIncomingMessage(playerJson, ClientId);

            // Act - Send protocol command (second packet)
            var protocolJson = "{\"context\":\"protocol\",\"data\":{\"protocol_version\":4,\"no_broadcast\":false}}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert - Client state is properly set
            var client = _authenticator.Client(ClientId);
            client.Should().NotBeNull();
            client.ClientPlatform.Should().Be(ClientOS.Android);
            client.ClientProtocolVersion.Should().Be(4);
            client.BroadcastsEnabled.Should().BeTrue();
            client.Authenticated.Should().BeTrue();

            // Assert - Responses were published
            _eventAggregator.PublishedMessages.Should().HaveCountGreaterOrEqualTo(2);
        }

        [Fact]
        public void FullHandshake_WithClientId_SetsClientIdentifier()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            var playerJson = "{\"context\":\"player\",\"data\":\"iOS\"}";
            _protocolHandler.ProcessIncomingMessage(playerJson, ClientId);

            var protocolJson = "{\"context\":\"protocol\",\"data\":{\"protocol_version\":4,\"client_id\":\"my-ios-app\"}}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.ClientId.Should().Be("my-ios-app");
            client.ClientPlatform.Should().Be(ClientOS.iOS);
        }

        [Fact]
        public void FullHandshake_LegacyV21Format_SetsCorrectCapabilities()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act - Legacy format with float version as string
            var playerJson = "{\"context\":\"player\",\"data\":\"Android\"}";
            _protocolHandler.ProcessIncomingMessage(playerJson, ClientId);

            var protocolJson = "{\"context\":\"protocol\",\"data\":\"2.1\"}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert - Client has V2.1 capabilities
            var client = _authenticator.Client(ClientId);
            client.ClientProtocolVersion.Should().Be(2); // Integer part
            client.Capabilities.SupportsPayloadObjects.Should().BeTrue();
            client.Capabilities.SupportsAutoDjShuffle.Should().BeTrue();
            client.Capabilities.SupportsFullPlayerStatus.Should().BeTrue();
            client.Capabilities.SupportsPagination.Should().BeFalse(); // V2.2+ only
        }

        [Fact]
        public void FullHandshake_LegacyV22Format_SetsAllCapabilities()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            var playerJson = "{\"context\":\"player\",\"data\":\"Android\"}";
            _protocolHandler.ProcessIncomingMessage(playerJson, ClientId);

            var protocolJson = "{\"context\":\"protocol\",\"data\":\"2.2\"}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert - Client has all V2.x capabilities
            var client = _authenticator.Client(ClientId);
            client.ClientProtocolVersion.Should().Be(2);
            client.Capabilities.SupportsPayloadObjects.Should().BeTrue();
            client.Capabilities.SupportsPagination.Should().BeTrue();
            client.Capabilities.SupportsAutoDjShuffle.Should().BeTrue();
            client.Capabilities.SupportsFullPlayerStatus.Should().BeTrue();
        }

        [Fact]
        public void FullHandshake_SimpleIntegerFormat_Works()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act - Simple integer format
            var playerJson = "{\"context\":\"player\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(playerJson, ClientId);

            var protocolJson = "{\"context\":\"protocol\",\"data\":3}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.ClientProtocolVersion.Should().Be(3);
            client.Capabilities.SupportsPayloadObjects.Should().BeTrue();
            client.Capabilities.SupportsPagination.Should().BeTrue();
        }

        #endregion

        #region Post-Handshake Command Tests

        [Fact]
        public void AfterHandshake_PingCommand_ReturnsPong()
        {
            // Arrange - Complete handshake first
            CompleteHandshake();
            _eventAggregator.Clear();

            // Act
            var pingJson = "{\"context\":\"ping\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(pingJson, ClientId);

            // Assert - Pong response was published
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void AfterHandshake_PluginVersionCommand_ReturnsVersion()
        {
            // Arrange
            CompleteHandshake();
            _eventAggregator.Clear();

            // Act
            var versionJson = "{\"context\":\"pluginversion\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(versionJson, ClientId);

            // Assert
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void AfterHandshake_UnknownCommand_DoesNotCrash()
        {
            // Arrange
            CompleteHandshake();
            _eventAggregator.Clear();

            // Act - Send unknown command
            var unknownJson = "{\"context\":\"unknowncommand\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(unknownJson, ClientId);

            // Assert - No crash, no response
            _eventAggregator.PublishedMessages.Should().BeEmpty();
        }

        #endregion

        #region Multiple Messages Tests

        [Fact]
        public void MultipleMessages_InSinglePayload_AllProcessed()
        {
            // Arrange
            CompleteHandshake();
            _eventAggregator.Clear();

            // Act - Send multiple commands in one payload
            var multiJson = "{\"context\":\"ping\",\"data\":\"\"}\r\n{\"context\":\"pluginversion\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(multiJson, ClientId);

            // Assert - Both commands processed
            _eventAggregator.PublishedMessages.Should().HaveCount(2);
        }

        [Fact]
        public void MultipleMessages_NewlineSeparated_AllProcessed()
        {
            // Arrange
            CompleteHandshake();
            _eventAggregator.Clear();

            // Act
            var multiJson = "{\"context\":\"ping\",\"data\":\"\"}\n{\"context\":\"ping\",\"data\":\"\"}\n{\"context\":\"ping\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(multiJson, ClientId);

            // Assert
            _eventAggregator.PublishedMessages.Should().HaveCount(3);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void MalformedJson_DoesNotCrash()
        {
            // Arrange
            CompleteHandshake();

            // Act - Send malformed JSON
            var malformedJson = "this is not json";
            _protocolHandler.ProcessIncomingMessage(malformedJson, ClientId);

            // Assert - No crash
            _authenticator.Client(ClientId).Should().NotBeNull();
        }

        [Fact]
        public void IncompleteJson_DoesNotCrash()
        {
            // Arrange
            CompleteHandshake();

            // Act - Send incomplete JSON
            var incompleteJson = "{\"context\":\"ping\"";
            _protocolHandler.ProcessIncomingMessage(incompleteJson, ClientId);

            // Assert - No crash
            _authenticator.Client(ClientId).Should().NotBeNull();
        }

        [Fact]
        public void EmptyData_HandledGracefully()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            var playerJson = "{\"context\":\"player\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(playerJson, ClientId);

            var protocolJson = "{\"context\":\"protocol\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert - Uses default values
            var client = _authenticator.Client(ClientId);
            client.ClientProtocolVersion.Should().Be(2); // Default
            client.ClientPlatform.Should().Be(ClientOS.Unknown);
        }

        [Fact]
        public void NullData_HandledGracefully()
        {
            // Arrange
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            var playerJson = "{\"context\":\"player\",\"data\":null}";
            _protocolHandler.ProcessIncomingMessage(playerJson, ClientId);

            var protocolJson = "{\"context\":\"protocol\",\"data\":null}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);

            // Assert
            var client = _authenticator.Client(ClientId);
            client.Should().NotBeNull();
            client.Authenticated.Should().BeTrue();
        }

        #endregion

        #region Verify Connection Tests

        [Fact]
        public void VerifyConnection_ReturnsImmediateResponse()
        {
            // Arrange - No handshake needed for verify connection
            _authenticator.AddClientOnConnect(ClientId);

            // Act
            var verifyJson = "{\"context\":\"verifyconnection\",\"data\":\"\"}";
            _protocolHandler.ProcessIncomingMessage(verifyJson, ClientId);

            // Assert - Response published via async
            _eventAggregator.AsyncPublishedMessages.Should().ContainSingle();
        }

        #endregion

        #region Client State Isolation Tests

        [Fact]
        public void MultipleClients_StateIsIsolated()
        {
            // Arrange
            const string client1 = "client-1";
            const string client2 = "client-2";

            _authenticator.AddClientOnConnect(client1);
            _authenticator.AddClientOnConnect(client2);

            // Act - Client 1: Android, V4
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", client1);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":4}", client1);

            // Act - Client 2: iOS, V2.1
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"iOS\"}", client2);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":\"2.1\"}", client2);

            // Assert - States are independent
            var c1 = _authenticator.Client(client1);
            var c2 = _authenticator.Client(client2);

            c1.ClientPlatform.Should().Be(ClientOS.Android);
            c1.ClientProtocolVersion.Should().Be(4);
            c1.Capabilities.SupportsPagination.Should().BeTrue();

            c2.ClientPlatform.Should().Be(ClientOS.iOS);
            c2.ClientProtocolVersion.Should().Be(2);
            c2.Capabilities.SupportsPagination.Should().BeFalse();
        }

        #endregion

        #region Helper Methods

        private void CompleteHandshake()
        {
            _authenticator.AddClientOnConnect(ClientId);

            var playerJson = "{\"context\":\"player\",\"data\":\"Android\"}";
            _protocolHandler.ProcessIncomingMessage(playerJson, ClientId);

            var protocolJson = "{\"context\":\"protocol\",\"data\":4}";
            _protocolHandler.ProcessIncomingMessage(protocolJson, ClientId);
        }

        #endregion
    }
}
