using System;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Processing;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Protocol
{
    public class ProtocolHandlerTests
    {
        private readonly Mock<IAuthenticator> _authenticator;
        private readonly Mock<ICommandDispatcher> _commandDispatcher;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly MockLogger _logger;
        private readonly ProtocolHandler _sut;

        public ProtocolHandlerTests()
        {
            _logger = new MockLogger();
            _authenticator = new Mock<IAuthenticator>();
            _eventAggregator = new Mock<IEventAggregator>();
            _commandDispatcher = new Mock<ICommandDispatcher>();

            // Default setup: return a new SocketClient for any client ID
            _authenticator.Setup(x => x.Client(It.IsAny<string>()))
                .Returns((string id) => new SocketClient(id));

            _sut = new ProtocolHandler(
                _logger,
                _authenticator.Object,
                _eventAggregator.Object,
                _commandDispatcher.Object);
        }

        [Fact]
        public void ProcessIncomingMessage_NullMessage_DoesNothing()
        {
            // Act
            _sut.ProcessIncomingMessage(null, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.IsAny<ICommandContext>()), Times.Never);
        }

        [Fact]
        public void ProcessIncomingMessage_EmptyMessage_DoesNothing()
        {
            // Act
            _sut.ProcessIncomingMessage(string.Empty, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.IsAny<ICommandContext>()), Times.Never);
        }

        [Fact]
        public void ProcessIncomingMessage_InvalidJson_DoesNotThrow()
        {
            // Act
            Action act = () => _sut.ProcessIncomingMessage("not valid json", "client1");

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void ProcessIncomingMessage_InvalidJsonStructure_DoesNotDispatch()
        {
            // Arrange
            var message = "not a json object";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.IsAny<ICommandContext>()), Times.Never);
        }

        [Fact]
        public void ProcessIncomingMessage_VerifyConnection_PublishesEvent()
        {
            // Arrange
            var message = "{\"context\":\"verifyconnection\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            _eventAggregator.Verify(x => x.PublishAsync(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void ProcessIncomingMessage_VerifyConnection_DoesNotDispatchCommand()
        {
            // Arrange
            var message = "{\"context\":\"verifyconnection\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.IsAny<ICommandContext>()), Times.Never);
        }

        [Fact]
        public void ProcessIncomingMessage_FirstPacketNotPlayer_TriggersDisconnect()
        {
            // Arrange
            var client = new SocketClient("client1"); // PacketNumber = 0
            _authenticator.Setup(x => x.Client("client1")).Returns(client);

            var disconnectTriggered = false;
            _sut.ForceClientDisconnect += (id) => disconnectTriggered = true;

            var message = "{\"context\":\"nowplayingtrack\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            disconnectTriggered.Should().BeTrue();
        }

        [Fact]
        public void ProcessIncomingMessage_FirstPacketIsPlayer_DispatchesCommand()
        {
            // Arrange
            var client = new SocketClient("client1"); // PacketNumber = 0
            _authenticator.Setup(x => x.Client("client1")).Returns(client);
            _commandDispatcher.Setup(x => x.Execute(It.IsAny<ICommandContext>())).Returns(true);

            var message = "{\"context\":\"player\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.Is<ICommandContext>(ctx => ctx.CommandType == "player")), Times.Once);
        }

        [Fact]
        public void ProcessIncomingMessage_SecondPacketNotProtocol_TriggersDisconnect()
        {
            // Arrange
            var client = new SocketClient("client1");
            client.IncreasePacketNumber(); // PacketNumber = 1
            _authenticator.Setup(x => x.Client("client1")).Returns(client);

            var disconnectTriggered = false;
            _sut.ForceClientDisconnect += (id) => disconnectTriggered = true;

            var message = "{\"context\":\"nowplayingtrack\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            disconnectTriggered.Should().BeTrue();
        }

        [Fact]
        public void ProcessIncomingMessage_SecondPacketIsProtocol_DispatchesCommand()
        {
            // Arrange
            var client = new SocketClient("client1");
            client.IncreasePacketNumber(); // PacketNumber = 1
            _authenticator.Setup(x => x.Client("client1")).Returns(client);
            _commandDispatcher.Setup(x => x.Execute(It.IsAny<ICommandContext>())).Returns(true);

            var message = "{\"context\":\"protocol\",\"data\":{\"protocol_version\":4}}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.Is<ICommandContext>(ctx => ctx.CommandType == "protocol")), Times.Once);
        }

        [Fact]
        public void ProcessIncomingMessage_AuthenticatedClient_DispatchesAnyCommand()
        {
            // Arrange
            var client = new SocketClient("client1");
            client.IncreasePacketNumber(); // 1
            client.IncreasePacketNumber(); // 2 - authenticated
            _authenticator.Setup(x => x.Client("client1")).Returns(client);
            _commandDispatcher.Setup(x => x.Execute(It.IsAny<ICommandContext>())).Returns(true);

            var message = "{\"context\":\"nowplayingtrack\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.Is<ICommandContext>(ctx => ctx.CommandType == "nowplayingtrack")), Times.Once);
        }

        [Fact]
        public void ProcessIncomingMessage_MultipleMessages_ProcessesAll()
        {
            // Arrange
            var client = new SocketClient("client1");
            client.IncreasePacketNumber();
            client.IncreasePacketNumber(); // authenticated
            _authenticator.Setup(x => x.Client("client1")).Returns(client);
            _commandDispatcher.Setup(x => x.Execute(It.IsAny<ICommandContext>())).Returns(true);

            var messages = "{\"context\":\"playervolume\",\"data\":50}\r\n{\"context\":\"playermute\",\"data\":false}";

            // Act
            _sut.ProcessIncomingMessage(messages, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.IsAny<ICommandContext>()), Times.Exactly(2));
        }

        [Fact]
        public void ProcessIncomingMessage_MessageWithNewlineOnly_ProcessesAll()
        {
            // Arrange
            var client = new SocketClient("client1");
            client.IncreasePacketNumber();
            client.IncreasePacketNumber();
            _authenticator.Setup(x => x.Client("client1")).Returns(client);
            _commandDispatcher.Setup(x => x.Execute(It.IsAny<ICommandContext>())).Returns(true);

            var messages = "{\"context\":\"ping\",\"data\":\"\"}\n{\"context\":\"pong\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(messages, "client1");

            // Assert
            _commandDispatcher.Verify(x => x.Execute(It.IsAny<ICommandContext>()), Times.Exactly(2));
        }

        [Fact]
        public void ProcessIncomingMessage_MessageWithObjectData_PassesDataCorrectly()
        {
            // Arrange
            var client = new SocketClient("client1");
            client.IncreasePacketNumber();
            client.IncreasePacketNumber();
            _authenticator.Setup(x => x.Client("client1")).Returns(client);

            ICommandContext capturedContext = null;
            _commandDispatcher.Setup(x => x.Execute(It.IsAny<ICommandContext>()))
                .Callback<ICommandContext>(ctx => capturedContext = ctx)
                .Returns(true);

            var message = "{\"context\":\"protocol\",\"data\":{\"protocol_version\":4,\"client_id\":\"test\"}}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            capturedContext.Should().NotBeNull();
            capturedContext.CommandType.Should().Be("protocol");
            capturedContext.Data.Should().NotBeNull();
        }

        [Fact]
        public void ProcessIncomingMessage_IncreasesPacketNumber()
        {
            // Arrange
            var client = new SocketClient("client1"); // PacketNumber = 0
            _authenticator.Setup(x => x.Client("client1")).Returns(client);
            _commandDispatcher.Setup(x => x.Execute(It.IsAny<ICommandContext>())).Returns(true);

            var message = "{\"context\":\"player\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            client.PacketNumber.Should().Be(1);
        }

        [Fact]
        public void ProcessIncomingMessage_LogsReceivedMessage()
        {
            // Arrange
            var message = "{\"context\":\"verifyconnection\",\"data\":\"\"}";

            // Act
            _sut.ProcessIncomingMessage(message, "client1");

            // Assert
            // ShortId only shows first 6 chars, so "client1" becomes "client"
            _logger.DebugMessages.Should().Contain(m => m.Contains("client") && m.Contains("verifyconnection"));
        }
    }
}
