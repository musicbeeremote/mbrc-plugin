using FluentAssertions;
using Moq;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Fixtures;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class SystemCommandsTests
    {
        private const string TestConnectionId = "test-connection-123";

        private readonly MockLogger _logger;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly Mock<IAuthenticator> _authenticator;
        private readonly Mock<IUserSettings> _userSettings;
        private readonly SystemCommands _sut;

        public SystemCommandsTests()
        {
            _logger = new MockLogger();
            _eventAggregator = new Mock<IEventAggregator>();
            _authenticator = new Mock<IAuthenticator>();
            _userSettings = new Mock<IUserSettings>();

            // Default user settings
            _userSettings.Setup(x => x.CurrentVersion).Returns("1.0.0");

            _sut = new SystemCommands(
                _logger,
                _eventAggregator.Object,
                _authenticator.Object,
                _userSettings.Object);
        }

        #region 4.1 Protocol - Simple Format

        [Fact]
        public void HandleProtocol_SimpleIntegerFormat_SetsProtocolVersion()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var innerContext = new TestCommandContext("protocol", 4, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(4);
            client.BroadcastsEnabled.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleProtocol_SimpleStringFormat_ParsesProtocolVersion()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var innerContext = new TestCommandContext("protocol", "4", TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void HandleProtocol_NullData_KeepsDefaultProtocolVersion()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var innerContext = new TestCommandContext("protocol", null, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(2); // Default value from SocketClient constructor
        }

        #endregion

        #region 4.2 Protocol - Object Format

        [Fact]
        public void HandleProtocol_ObjectFormat_SetsProtocolVersion()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var data = JObject.FromObject(new { protocol_version = 4 });
            var innerContext = new TestCommandContext("protocol", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void HandleProtocol_ObjectFormatWithNoBroadcast_DisablesBroadcasts()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var data = JObject.FromObject(new { protocol_version = 5, no_broadcast = true });
            var innerContext = new TestCommandContext("protocol", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.BroadcastsEnabled.Should().BeFalse();
        }

        [Fact]
        public void HandleProtocol_ObjectFormatWithNoBroadcastFalse_EnablesBroadcasts()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            client.BroadcastsEnabled = false; // Start disabled
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var data = JObject.FromObject(new { protocol_version = 5, no_broadcast = false });
            var innerContext = new TestCommandContext("protocol", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.BroadcastsEnabled.Should().BeTrue();
        }

        [Fact]
        public void HandleProtocol_ObjectFormatWithClientId_SetsClientId()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var data = JObject.FromObject(new { protocol_version = 5, client_id = "android-client-001" });
            var innerContext = new TestCommandContext("protocol", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientId.Should().Be("android-client-001");
        }

        [Fact]
        public void HandleProtocol_ObjectFormatNoNoBroadcast_DefaultsToEnabled()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            client.BroadcastsEnabled = false; // Start disabled
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var data = JObject.FromObject(new { protocol_version = 5 });
            var innerContext = new TestCommandContext("protocol", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.BroadcastsEnabled.Should().BeTrue();
        }

        #endregion

        #region 4.3 Protocol - Error Cases

        [Fact]
        public void HandleProtocol_ClientNotFound_ReturnsFalse()
        {
            // Arrange
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns((SocketClient)null);
            var innerContext = new TestCommandContext("protocol", 5, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeFalse();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public void HandleProtocol_InvalidStringData_KeepsDefaultVersion()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var innerContext = new TestCommandContext("protocol", "invalid", TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.ProtocolHandshakeRequest>(innerContext);

            // Act
            var result = _sut.HandleProtocol(context);

            // Assert
            result.Should().BeTrue();
            client.ClientProtocolVersion.Should().Be(2); // Default value
        }

        #endregion

        #region 4.4 Player Command

        [Fact]
        public void HandlePlayer_AndroidPlatform_SetsClientPlatform()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var context = new TestCommandContext("player", "Android", TestConnectionId);

            // Act
            var result = _sut.HandlePlayer(context);

            // Assert
            result.Should().BeTrue();
            client.ClientPlatform.Should().Be(ClientOS.Android);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlayer_IosPlatform_SetsClientPlatform()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var context = new TestCommandContext("player", "iOS", TestConnectionId);

            // Act
            var result = _sut.HandlePlayer(context);

            // Assert
            result.Should().BeTrue();
            client.ClientPlatform.Should().Be(ClientOS.iOS);
        }

        [Fact]
        public void HandlePlayer_UnknownPlatform_SetsUnknown()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var context = new TestCommandContext("player", "Windows", TestConnectionId);

            // Act
            var result = _sut.HandlePlayer(context);

            // Assert
            result.Should().BeTrue();
            client.ClientPlatform.Should().Be(ClientOS.Unknown);
        }

        [Fact]
        public void HandlePlayer_CaseInsensitive_SetsCorrectPlatform()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var context = new TestCommandContext("player", "android", TestConnectionId);

            // Act
            var result = _sut.HandlePlayer(context);

            // Assert
            result.Should().BeTrue();
            client.ClientPlatform.Should().Be(ClientOS.Android);
        }

        [Fact]
        public void HandlePlayer_NullClient_StillReturnsTrue()
        {
            // Arrange
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns((SocketClient)null);
            var context = new TestCommandContext("player", "Android", TestConnectionId);

            // Act
            var result = _sut.HandlePlayer(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlayer_NullPlatform_SetsUnknown()
        {
            // Arrange
            var client = new SocketClient(TestConnectionId);
            _authenticator.Setup(x => x.Client(TestConnectionId)).Returns(client);
            var context = new TestCommandContext("player", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlayer(context);

            // Assert
            result.Should().BeTrue();
            client.ClientPlatform.Should().Be(ClientOS.Unknown);
        }

        #endregion

        #region 4.5 Plugin Version

        [Fact]
        public void HandlePluginVersion_ReturnsCurrentVersion()
        {
            // Arrange
            _userSettings.Setup(x => x.CurrentVersion).Returns("2.5.0");
            var context = new TestCommandContext("pluginversion", null, TestConnectionId);

            // Act
            var result = _sut.HandlePluginVersion(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePluginVersion_WithData_IgnoresDataAndReturnsVersion()
        {
            // Arrange
            _userSettings.Setup(x => x.CurrentVersion).Returns("1.0.0");
            var context = new TestCommandContext("pluginversion", "somedata", TestConnectionId);

            // Act
            var result = _sut.HandlePluginVersion(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 4.6 Ping/Pong

        [Fact]
        public void HandlePing_SendsPongResponse()
        {
            // Arrange
            var context = new TestCommandContext("ping", null, TestConnectionId);

            // Act
            var result = _sut.HandlePing(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePing_WithData_IgnoresDataAndSendsPong()
        {
            // Arrange
            var context = new TestCommandContext("ping", "some-timestamp", TestConnectionId);

            // Act
            var result = _sut.HandlePing(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePong_LogsReceipt_ReturnsTrue()
        {
            // Arrange
            var context = new TestCommandContext("pong", null, TestConnectionId);

            // Act
            var result = _sut.HandlePong(context);

            // Assert
            result.Should().BeTrue();
            // Pong doesn't publish anything, just logs
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public void HandlePong_WithTimestamp_ReturnsTrue()
        {
            // Arrange
            var context = new TestCommandContext("pong", "1234567890", TestConnectionId);

            // Act
            var result = _sut.HandlePong(context);

            // Assert
            result.Should().BeTrue();
        }

        #endregion
    }
}
