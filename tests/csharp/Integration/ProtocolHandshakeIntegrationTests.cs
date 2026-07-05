using FluentAssertions;
using Moq;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Fixtures;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    /// Integration tests for the protocol handshake sequence and version negotiation.
    /// Tests the full flow: player command → protocol command → capability checks.
    /// </summary>
    public class ProtocolHandshakeIntegrationTests
    {
        private const string TestConnectionId = "test-connection-001";

        private readonly MockAuthenticator _authenticator;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly MockLogger _logger;
        private readonly SystemCommands _systemCommands;
        private readonly ProtocolCapabilities _capabilities;

        public ProtocolHandshakeIntegrationTests()
        {
            _authenticator = new MockAuthenticator();
            _eventAggregator = new Mock<IEventAggregator>();
            _logger = new MockLogger();
            var userSettings = new Mock<IUserSettings>();
            userSettings.Setup(x => x.CurrentVersion).Returns("1.0.0");

            _systemCommands = new SystemCommands(_logger, _eventAggregator.Object, _authenticator, userSettings.Object);
            _capabilities = new ProtocolCapabilities(_authenticator);
        }

        #region Handshake Sequence Tests

        [Fact]
        public void FullHandshake_PlayerThenProtocol_SetsClientState()
        {
            // Arrange
            var client = CreateAndRegisterClient();

            // Act - Step 1: Player command
            var playerContext = new TestCommandContext(ProtocolConstants.Player, "Android", TestConnectionId);
            var playerResult = _systemCommands.HandlePlayer(playerContext);

            // Act - Step 2: Protocol command
            var protocolData = JObject.FromObject(new { protocol_version = 4, no_broadcast = false, client_id = "android-001" });
            var protocolContext = CreateTypedProtocolContext(protocolData);
            var protocolResult = _systemCommands.HandleProtocol(protocolContext);

            // Assert
            playerResult.Should().BeTrue();
            protocolResult.Should().BeTrue();
            client.ClientPlatform.Should().Be(ClientOS.Android);
            client.ClientProtocolVersion.Should().Be(4);
            client.BroadcastsEnabled.Should().BeTrue();
            client.ClientId.Should().Be("android-001");
        }

        [Fact]
        public void FullHandshake_WithNoBroadcast_DisablesBroadcasts()
        {
            // Arrange
            var client = CreateAndRegisterClient();

            // Act
            var protocolData = JObject.FromObject(new { protocol_version = 4, no_broadcast = true });
            var protocolContext = CreateTypedProtocolContext(protocolData);
            _systemCommands.HandleProtocol(protocolContext);

            // Assert
            client.BroadcastsEnabled.Should().BeFalse();
        }

        #endregion

        #region Legacy Float Version Tests

        [Theory]
        [InlineData("2.1", 2)]  // 2.1 maps to V2 (integer part)
        [InlineData("2.2", 2)]  // 2.2 maps to V2 (integer part)
        public void ProtocolHandshake_LegacyFloatString_MapsToIntegerPart(string version, int expectedVersion)
        {
            // Arrange
            var client = CreateAndRegisterClient();
            var context = CreateTypedProtocolContext(version);

            // Act
            var result = _systemCommands.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(expectedVersion);
        }

        [Fact]
        public void ProtocolHandshake_LegacyFloatDouble_MapsToIntegerPart()
        {
            // Arrange
            var client = CreateAndRegisterClient();
            var jValue = new JValue(2.1);
            var context = CreateTypedProtocolContext(jValue);

            // Act
            var result = _systemCommands.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(2); // Maps to integer part
        }

        [Fact]
        public void ProtocolHandshake_LegacyV2_StaysV2()
        {
            // Arrange
            var client = CreateAndRegisterClient();
            var context = CreateTypedProtocolContext(2);

            // Act
            var result = _systemCommands.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(2);
        }

        #endregion

        #region Version Negotiation and Capabilities

        [Fact]
        public void AfterHandshake_V2Client_DoesNotSupportV3Features()
        {
            // Arrange
            CreateAndRegisterClient();
            var context = CreateTypedProtocolContext(2);
            _systemCommands.HandleProtocol(context);

            // Act & Assert - V2 clients don't support V3+ features via current capability checks
            _capabilities.SupportsPayloadObjects(TestConnectionId).Should().BeFalse();
            _capabilities.SupportsPagination(TestConnectionId).Should().BeFalse();
            _capabilities.SupportsAutoDjShuffle(TestConnectionId).Should().BeFalse();
        }

        [Fact]
        public void AfterHandshake_V3Client_SupportsAllFeatures()
        {
            // Arrange
            CreateAndRegisterClient();
            var context = CreateTypedProtocolContext(3);
            _systemCommands.HandleProtocol(context);

            // Act & Assert
            _capabilities.SupportsPayloadObjects(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsPagination(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsAutoDjShuffle(TestConnectionId).Should().BeTrue();
        }

        [Fact]
        public void AfterHandshake_LegacyV21Client_SupportsV21Features()
        {
            // Arrange - Legacy 2.1 maps to V2 (integer part) but has V2.1 capabilities
            CreateAndRegisterClient();
            var context = CreateTypedProtocolContext("2.1");
            _systemCommands.HandleProtocol(context);

            // Assert - V2.1 supports payload objects and AutoDJ shuffle
            _capabilities.SupportsPayloadObjects(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsAutoDjShuffle(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsFullPlayerStatus(TestConnectionId).Should().BeTrue();
            // But not pagination (that's V2.2+)
            _capabilities.SupportsPagination(TestConnectionId).Should().BeFalse();
        }

        [Fact]
        public void AfterHandshake_LegacyV22Client_SupportsAllV2Features()
        {
            // Arrange - Legacy 2.2 maps to V2 but has full V2.x capabilities
            CreateAndRegisterClient();
            var context = CreateTypedProtocolContext("2.2");
            _systemCommands.HandleProtocol(context);

            // Assert - V2.2 supports all features including pagination
            _capabilities.SupportsPayloadObjects(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsPagination(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsAutoDjShuffle(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsFullPlayerStatus(TestConnectionId).Should().BeTrue();
        }

        [Fact]
        public void AfterHandshake_V4Client_SupportsAllFeatures()
        {
            // Arrange
            CreateAndRegisterClient();
            var protocolData = JObject.FromObject(new { protocol_version = 4 });
            var context = CreateTypedProtocolContext(protocolData);
            _systemCommands.HandleProtocol(context);

            // Act & Assert
            _capabilities.SupportsPayloadObjects(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsPagination(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsAutoDjShuffle(TestConnectionId).Should().BeTrue();
            _capabilities.SupportsFullPlayerStatus(TestConnectionId).Should().BeTrue();
        }

        #endregion

        #region Response Verification

        [Fact]
        public void ProtocolHandshake_PublishesServerProtocolVersion()
        {
            // Arrange
            CreateAndRegisterClient();
            var context = CreateTypedProtocolContext(4);

            // Act
            _systemCommands.HandleProtocol(context);

            // Assert - Server should respond with its protocol version
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void PlayerCommand_PublishesPlayerName()
        {
            // Arrange
            CreateAndRegisterClient();
            var context = new TestCommandContext(ProtocolConstants.Player, "Android", TestConnectionId);

            // Act
            _systemCommands.HandlePlayer(context);

            // Assert
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region Format Compatibility Tests

        [Fact]
        public void ProtocolHandshake_SimpleIntegerFormat_Works()
        {
            // Arrange - Simple format: just the version number
            var client = CreateAndRegisterClient();
            var context = CreateTypedProtocolContext(4);

            // Act
            var result = _systemCommands.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void ProtocolHandshake_SimpleStringFormat_Works()
        {
            // Arrange - String format: "4"
            var client = CreateAndRegisterClient();
            var context = CreateTypedProtocolContext("4");

            // Act
            var result = _systemCommands.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void ProtocolHandshake_ObjectFormat_Works()
        {
            // Arrange - Object format with all fields
            var client = CreateAndRegisterClient();
            var data = JObject.FromObject(new
            {
                protocol_version = 4,
                no_broadcast = true,
                client_id = "test-client"
            });
            var context = CreateTypedProtocolContext(data);

            // Act
            var result = _systemCommands.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(4);
            client.BroadcastsEnabled.Should().BeFalse();
            client.ClientId.Should().Be("test-client");
        }

        [Fact]
        public void ProtocolHandshake_ObjectFormatPartial_Works()
        {
            // Arrange - Object format with only version
            var client = CreateAndRegisterClient();
            var data = JObject.FromObject(new { protocol_version = 3 });
            var context = CreateTypedProtocolContext(data);

            // Act
            var result = _systemCommands.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(3);
            client.BroadcastsEnabled.Should().BeTrue(); // Default
        }

        #endregion

        #region Platform Detection Tests

        [Theory]
        [InlineData("Android", ClientOS.Android)]
        [InlineData("android", ClientOS.Android)]
        [InlineData("iOS", ClientOS.iOS)]
        [InlineData("ios", ClientOS.iOS)]
        [InlineData("unknown", ClientOS.Unknown)]
        [InlineData("", ClientOS.Unknown)]
        public void PlayerCommand_SetsPlatformCorrectly(string platform, ClientOS expected)
        {
            // Arrange
            var client = CreateAndRegisterClient();
            var context = new TestCommandContext(ProtocolConstants.Player, platform, TestConnectionId);

            // Act
            _systemCommands.HandlePlayer(context);

            // Assert
            client.ClientPlatform.Should().Be(expected);
        }

        #endregion

        #region Helper Methods

        private SocketClient CreateAndRegisterClient()
        {
            var client = new SocketClient(TestConnectionId);
            _authenticator.AddClient(client);
            return client;
        }

        private static TypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest> CreateTypedProtocolContext(object data)
        {
            var innerContext = new TestCommandContext(ProtocolConstants.Protocol, data, TestConnectionId);
            return new TypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);
        }

        #endregion
    }
}
