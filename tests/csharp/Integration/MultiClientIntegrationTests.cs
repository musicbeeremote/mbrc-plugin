using System.Collections.Generic;
using System.Linq;
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
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    /// Integration tests for multiple clients connected simultaneously with different protocol versions.
    ///
    /// These tests verify:
    /// - V2, V2.1, V2.2, V3, V4 clients can connect concurrently
    /// - Each client maintains isolated state
    /// - Capabilities are correctly set per client
    /// - Broadcasts deliver appropriate format to each client
    /// </summary>
    public class MultiClientIntegrationTests
    {
        private readonly Authenticator _authenticator;
        private readonly DelegateCommandDispatcher _dispatcher;
        private readonly MockEventAggregator _eventAggregator;
        private readonly MockLogger _logger;
        private readonly ProtocolHandler _protocolHandler;
        private readonly SystemCommands _systemCommands;
        private readonly ProtocolCapabilities _capabilities;

        public MultiClientIntegrationTests()
        {
            _logger = new MockLogger();
            _authenticator = new Authenticator();
            _eventAggregator = new MockEventAggregator();
            _dispatcher = new DelegateCommandDispatcher(_logger);
            _capabilities = new ProtocolCapabilities(_authenticator);

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
            registrar.RegisterCommand(ProtocolConstants.Ping, _systemCommands.HandlePing);
        }

        #region Concurrent Client Connection Tests

        [Fact]
        public void FiveClients_DifferentVersions_AllConnectSuccessfully()
        {
            // Arrange & Act - Connect 5 clients with different versions
            var clients = new[]
            {
                ("client-v2", "Android", "2"),
                ("client-v21", "Android", "2.1"),
                ("client-v22", "iOS", "2.2"),
                ("client-v3", "Android", "3"),
                ("client-v4", "iOS", "4")
            };

            foreach (var (clientId, platform, version) in clients)
            {
                ConnectClient(clientId, platform, version);
            }

            // Assert - All clients are connected and authenticated
            foreach (var (clientId, _, _) in clients)
            {
                var client = _authenticator.Client(clientId);
                client.Should().NotBeNull();
                client.Authenticated.Should().BeTrue();
            }
        }

        [Fact]
        public void MultipleClients_EachHasCorrectProtocolVersion()
        {
            // Arrange & Act
            ConnectClient("v2-client", "Android", "2");
            ConnectClient("v3-client", "iOS", "3");
            ConnectClient("v4-client", "Android", "4");

            // Assert
            _authenticator.Client("v2-client").ClientProtocolVersion.Should().Be(2);
            _authenticator.Client("v3-client").ClientProtocolVersion.Should().Be(3);
            _authenticator.Client("v4-client").ClientProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void MultipleClients_EachHasCorrectPlatform()
        {
            // Arrange & Act
            ConnectClient("android-1", "Android", "4");
            ConnectClient("ios-1", "iOS", "4");
            ConnectClient("android-2", "android", "4"); // lowercase
            ConnectClient("unknown-1", "Windows", "4");

            // Assert
            _authenticator.Client("android-1").ClientPlatform.Should().Be(ClientOS.Android);
            _authenticator.Client("ios-1").ClientPlatform.Should().Be(ClientOS.iOS);
            _authenticator.Client("android-2").ClientPlatform.Should().Be(ClientOS.Android);
            _authenticator.Client("unknown-1").ClientPlatform.Should().Be(ClientOS.Unknown);
        }

        #endregion

        #region Capability Isolation Tests

        [Fact]
        public void MultipleClients_CapabilitiesAreIsolated()
        {
            // Arrange & Act
            ConnectClient("v2-client", "Android", "2");
            ConnectClient("v21-client", "Android", "2.1");
            ConnectClient("v22-client", "Android", "2.2");
            ConnectClient("v3-client", "Android", "3");

            // Assert - V2 client has no capabilities
            _capabilities.SupportsPayloadObjects("v2-client").Should().BeFalse();
            _capabilities.SupportsPagination("v2-client").Should().BeFalse();
            _capabilities.SupportsAutoDjShuffle("v2-client").Should().BeFalse();

            // Assert - V2.1 client has some capabilities
            _capabilities.SupportsPayloadObjects("v21-client").Should().BeTrue();
            _capabilities.SupportsPagination("v21-client").Should().BeFalse();
            _capabilities.SupportsAutoDjShuffle("v21-client").Should().BeTrue();

            // Assert - V2.2 client has more capabilities
            _capabilities.SupportsPayloadObjects("v22-client").Should().BeTrue();
            _capabilities.SupportsPagination("v22-client").Should().BeTrue();

            // Assert - V3 client has all capabilities
            _capabilities.SupportsPayloadObjects("v3-client").Should().BeTrue();
            _capabilities.SupportsPagination("v3-client").Should().BeTrue();
            _capabilities.SupportsAutoDjShuffle("v3-client").Should().BeTrue();
            _capabilities.SupportsFullPlayerStatus("v3-client").Should().BeTrue();
        }

        [Fact]
        public void ClientCapabilities_NotAffectedByOtherClients()
        {
            // Arrange - Connect V2 client first
            ConnectClient("first-v2", "Android", "2");
            _capabilities.SupportsPayloadObjects("first-v2").Should().BeFalse();

            // Act - Connect V4 client
            ConnectClient("second-v4", "Android", "4");

            // Assert - V2 client capabilities unchanged
            _capabilities.SupportsPayloadObjects("first-v2").Should().BeFalse();
            _capabilities.SupportsPayloadObjects("second-v4").Should().BeTrue();
        }

        #endregion

        #region Broadcast Delivery Tests

        [Fact]
        public void Broadcast_MultipleClients_EachGetsCorrectFormat()
        {
            // Arrange
            ConnectClient("v2-client", "Android", "2");
            ConnectClient("v3-client", "Android", "3");
            ConnectClient("v4-client", "Android", "4");

            // Act - Create a broadcast event
            var broadcastEvent = new BroadcastEvent("nowplayinglyrics");
            broadcastEvent.AddPayload(2, "Raw lyrics text");
            broadcastEvent.AddPayload(3, new { status = 200, lyrics = "Raw lyrics text" });

            // Assert - Each client version gets appropriate format
            var v2Message = broadcastEvent.GetMessage(2);
            var v3Message = broadcastEvent.GetMessage(3);
            var v4Message = broadcastEvent.GetMessage(4);

            // V2 gets raw string
            var v2Parsed = JObject.Parse(v2Message);
            v2Parsed["data"].Type.Should().Be(JTokenType.String);

            // V3 and V4 get object
            var v3Parsed = JObject.Parse(v3Message);
            v3Parsed["data"].Type.Should().Be(JTokenType.Object);

            var v4Parsed = JObject.Parse(v4Message);
            v4Parsed["data"].Type.Should().Be(JTokenType.Object);
        }

        [Fact]
        public void Broadcast_LegacyClients_GetV2Format()
        {
            // Arrange
            ConnectClient("v2-client", "Android", "2");
            ConnectClient("v21-client", "Android", "2.1");
            ConnectClient("v22-client", "Android", "2.2");

            var broadcastEvent = new BroadcastEvent("test");
            broadcastEvent.AddPayload(2, "v2-data");
            broadcastEvent.AddPayload(3, "v3-data");

            // Act & Assert - All legacy clients (protocol version 2) get V2 format
            var v2Client = _authenticator.Client("v2-client");
            var v21Client = _authenticator.Client("v21-client");
            var v22Client = _authenticator.Client("v22-client");

            // All have protocol version 2 (integer part)
            v2Client.ClientProtocolVersion.Should().Be(2);
            v21Client.ClientProtocolVersion.Should().Be(2);
            v22Client.ClientProtocolVersion.Should().Be(2);

            // All get V2 broadcast format
            broadcastEvent.GetMessage(v2Client.ClientProtocolVersion).Should().Contain("v2-data");
            broadcastEvent.GetMessage(v21Client.ClientProtocolVersion).Should().Contain("v2-data");
            broadcastEvent.GetMessage(v22Client.ClientProtocolVersion).Should().Contain("v2-data");
        }

        #endregion

        #region Client State Independence Tests

        [Fact]
        public void ClientState_ModifyingOneDoesNotAffectOthers()
        {
            // Arrange
            ConnectClient("client-a", "Android", "4");
            ConnectClient("client-b", "iOS", "3");

            var clientA = _authenticator.Client("client-a");
            var clientB = _authenticator.Client("client-b");

            // Act - Modify client A's state
            clientA.BroadcastsEnabled = false;

            // Assert - Client B is not affected
            clientB.BroadcastsEnabled.Should().BeTrue();
            clientA.BroadcastsEnabled.Should().BeFalse();
        }

        [Fact]
        public void ClientId_UniquePerClient()
        {
            // Arrange - Connect clients with client_id
            _authenticator.AddClientOnConnect("conn-1");
            _authenticator.AddClientOnConnect("conn-2");

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", "conn-1");
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":{\"protocol_version\":4,\"client_id\":\"app-instance-1\"}}", "conn-1");

            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"iOS\"}", "conn-2");
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"protocol\",\"data\":{\"protocol_version\":4,\"client_id\":\"app-instance-2\"}}", "conn-2");

            // Assert
            _authenticator.Client("conn-1").ClientId.Should().Be("app-instance-1");
            _authenticator.Client("conn-2").ClientId.Should().Be("app-instance-2");
        }

        #endregion

        #region Client Disconnect Tests

        [Fact]
        public void ClientDisconnect_DoesNotAffectOtherClients()
        {
            // Arrange
            ConnectClient("client-1", "Android", "4");
            ConnectClient("client-2", "Android", "4");
            ConnectClient("client-3", "Android", "4");

            // Act - Disconnect client-2
            _authenticator.RemoveClientOnDisconnect("client-2");

            // Assert
            _authenticator.Client("client-1").Should().NotBeNull();
            _authenticator.Client("client-2").Should().BeNull();
            _authenticator.Client("client-3").Should().NotBeNull();
        }

        [Fact]
        public void MultipleDisconnects_RemainingClientsUnaffected()
        {
            // Arrange
            var clientIds = Enumerable.Range(1, 10).Select(i => $"client-{i}").ToList();
            foreach (var clientId in clientIds)
            {
                ConnectClient(clientId, "Android", "4");
            }

            // Act - Disconnect odd-numbered clients
            foreach (var clientId in clientIds.Where((_, i) => i % 2 == 0))
            {
                _authenticator.RemoveClientOnDisconnect(clientId);
            }

            // Assert - Even-numbered clients still connected
            foreach (var clientId in clientIds.Where((_, i) => i % 2 == 1))
            {
                _authenticator.Client(clientId).Should().NotBeNull();
                _authenticator.Client(clientId).Authenticated.Should().BeTrue();
            }
        }

        #endregion

        #region Reconnection Tests

        [Fact]
        public void ClientReconnect_GetsNewState()
        {
            // Arrange - Initial connection
            ConnectClient("reconnecting-client", "Android", "3");
            var initialClient = _authenticator.Client("reconnecting-client");
            initialClient.ClientProtocolVersion.Should().Be(3);

            // Act - Disconnect and reconnect with different version
            _authenticator.RemoveClientOnDisconnect("reconnecting-client");
            ConnectClient("reconnecting-client", "iOS", "4");

            // Assert - New state
            var newClient = _authenticator.Client("reconnecting-client");
            newClient.ClientProtocolVersion.Should().Be(4);
            newClient.ClientPlatform.Should().Be(ClientOS.iOS);
        }

        #endregion

        #region Stress/Edge Cases

        [Fact]
        public void ManyClients_AllMaintainCorrectState()
        {
            // Arrange & Act - Connect 20 clients with varying versions
            var versions = new[] { "2", "2.1", "2.2", "3", "4" };
            var platforms = new[] { "Android", "iOS" };

            for (var i = 0; i < 20; i++)
            {
                var version = versions[i % versions.Length];
                var platform = platforms[i % platforms.Length];
                ConnectClient($"client-{i}", platform, version);
            }

            // Assert - All clients have correct state
            for (var i = 0; i < 20; i++)
            {
                var client = _authenticator.Client($"client-{i}");
                client.Should().NotBeNull();
                client.Authenticated.Should().BeTrue();
            }
        }

        [Fact]
        public void SimultaneousCommands_ProcessedCorrectly()
        {
            // Arrange
            ConnectClient("client-a", "Android", "4");
            ConnectClient("client-b", "iOS", "4");
            _eventAggregator.Clear();

            // Act - Send commands from both clients
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", "client-a");
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"ping\",\"data\":\"\"}", "client-b");

            // Assert - Both commands processed
            _eventAggregator.PublishedMessages.Should().HaveCount(2);
        }

        #endregion

        #region Helper Methods

        private void ConnectClient(string clientId, string platform, string protocolVersion)
        {
            _authenticator.AddClientOnConnect(clientId);

            var playerJson = $"{{\"context\":\"player\",\"data\":\"{platform}\"}}";
            _protocolHandler.ProcessIncomingMessage(playerJson, clientId);

            string protocolJson;
            if (protocolVersion.Contains('.'))
            {
                // Legacy float version as string
                protocolJson = $"{{\"context\":\"protocol\",\"data\":\"{protocolVersion}\"}}";
            }
            else
            {
                // Integer version
                protocolJson = $"{{\"context\":\"protocol\",\"data\":{protocolVersion}}}";
            }
            _protocolHandler.ProcessIncomingMessage(protocolJson, clientId);
        }

        #endregion
    }
}
