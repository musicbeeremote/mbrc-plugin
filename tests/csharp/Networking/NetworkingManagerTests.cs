using System;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Networking
{
    public class NetworkingManagerTests : IDisposable
    {
        private readonly Mock<ISocketServer> _socketServer;
        private readonly Mock<IServiceDiscovery> _serviceDiscovery;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly MockLogger _logger;
        private readonly NetworkingManager _sut;

        private Action<MessageSendEvent> _messageSendHandler;
        private Action<BroadcastEvent> _broadcastHandler;

        public NetworkingManagerTests()
        {
            _socketServer = new Mock<ISocketServer>();
            _serviceDiscovery = new Mock<IServiceDiscovery>();
            _eventAggregator = new Mock<IEventAggregator>();
            _logger = new MockLogger();

            // Capture the subscription handlers
            _eventAggregator
                .Setup(x => x.Subscribe<MessageSendEvent>(It.IsAny<Action<MessageSendEvent>>()))
                .Callback<Action<MessageSendEvent>>(handler => _messageSendHandler = handler)
                .Returns(Mock.Of<IDisposable>());

            _eventAggregator
                .Setup(x => x.Subscribe<BroadcastEvent>(It.IsAny<Action<BroadcastEvent>>()))
                .Callback<Action<BroadcastEvent>>(handler => _broadcastHandler = handler)
                .Returns(Mock.Of<IDisposable>());

            _sut = new NetworkingManager(
                _socketServer.Object,
                _serviceDiscovery.Object,
                _eventAggregator.Object,
                _logger);
        }

        public void Dispose()
        {
            _sut?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region 7.1 Constructor and Subscriptions

        [Fact]
        public void Constructor_SubscribesToMessageSendEvent()
        {
            // Assert
            _eventAggregator.Verify(
                x => x.Subscribe<MessageSendEvent>(It.IsAny<Action<MessageSendEvent>>()),
                Times.Once);
        }

        [Fact]
        public void Constructor_SubscribesToBroadcastEvent()
        {
            // Assert
            _eventAggregator.Verify(
                x => x.Subscribe<BroadcastEvent>(It.IsAny<Action<BroadcastEvent>>()),
                Times.Once);
        }

        #endregion

        #region 7.2 IsRunning Property

        [Fact]
        public void IsRunning_ReturnsSocketServerIsRunning()
        {
            // Arrange
            _socketServer.Setup(x => x.IsRunning).Returns(true);

            // Act
            var result = _sut.IsRunning;

            // Assert
            result.Should().BeTrue();
            _socketServer.Verify(x => x.IsRunning, Times.Once);
        }

        [Fact]
        public void IsRunning_WhenNotRunning_ReturnsFalse()
        {
            // Arrange
            _socketServer.Setup(x => x.IsRunning).Returns(false);

            // Act
            var result = _sut.IsRunning;

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region 7.3 StartListening

        [Fact]
        public void StartListening_StartsServiceDiscoveryFirst()
        {
            // Arrange
            var callOrder = 0;
            var discoveryOrder = 0;
            var socketOrder = 0;

            _serviceDiscovery.Setup(x => x.StartListening())
                .Callback(() => discoveryOrder = ++callOrder);
            _socketServer.Setup(x => x.StartListening())
                .Callback(() => socketOrder = ++callOrder);

            // Act
            _sut.StartListening();

            // Assert
            discoveryOrder.Should().Be(1);
            socketOrder.Should().Be(2);
        }

        [Fact]
        public void StartListening_StartsSocketServer()
        {
            // Act
            _sut.StartListening();

            // Assert
            _socketServer.Verify(x => x.StartListening(), Times.Once);
        }

        [Fact]
        public void StartListening_StartsServiceDiscovery()
        {
            // Act
            _sut.StartListening();

            // Assert
            _serviceDiscovery.Verify(x => x.StartListening(), Times.Once);
        }

        #endregion

        #region 7.4 StopListening

        [Fact]
        public void StopListening_StopsSocketServerFirst()
        {
            // Arrange
            var callOrder = 0;
            var socketOrder = 0;
            var discoveryOrder = 0;

            _socketServer.Setup(x => x.StopListening())
                .Callback(() => socketOrder = ++callOrder);
            _serviceDiscovery.Setup(x => x.StopListening())
                .Callback(() => discoveryOrder = ++callOrder);

            // Act
            _sut.StopListening();

            // Assert
            socketOrder.Should().Be(1);
            discoveryOrder.Should().Be(2);
        }

        [Fact]
        public void StopListening_StopsSocketServer()
        {
            // Act
            _sut.StopListening();

            // Assert
            _socketServer.Verify(x => x.StopListening(), Times.Once);
        }

        [Fact]
        public void StopListening_StopsServiceDiscovery()
        {
            // Act
            _sut.StopListening();

            // Assert
            _serviceDiscovery.Verify(x => x.StopListening(), Times.Once);
        }

        #endregion

        #region 7.5 Restart

        [Fact]
        public void Restart_RestartsSocketServer()
        {
            // Act
            _sut.Restart();

            // Assert
            _socketServer.Verify(x => x.RestartSocket(), Times.Once);
        }

        #endregion

        #region 7.6 MessageSendEvent Handling

        [Fact]
        public void MessageSendEvent_ForwardsToSocketServer()
        {
            // Arrange
            var messageEvent = new MessageSendEvent("test message", "client-123");

            // Act
            _messageSendHandler?.Invoke(messageEvent);

            // Assert
            _socketServer.Verify(x => x.Send("test message", "client-123"), Times.Once);
        }

        [Fact]
        public void MessageSendEvent_EmptyMessage_DoesNotForward()
        {
            // Arrange
            var messageEvent = new MessageSendEvent(string.Empty, "client-123");

            // Act
            _messageSendHandler?.Invoke(messageEvent);

            // Assert
            _socketServer.Verify(x => x.Send(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void MessageSendEvent_NullMessage_DoesNotForward()
        {
            // Arrange
            var messageEvent = new MessageSendEvent(null, "client-123");

            // Act
            _messageSendHandler?.Invoke(messageEvent);

            // Assert
            _socketServer.Verify(x => x.Send(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void MessageSendEvent_BroadcastToAll_ForwardsCorrectly()
        {
            // Arrange
            var messageEvent = new MessageSendEvent("broadcast message");

            // Act
            _messageSendHandler?.Invoke(messageEvent);

            // Assert
            _socketServer.Verify(x => x.Send("broadcast message", "all"), Times.Once);
        }

        #endregion

        #region 7.7 BroadcastEvent Handling

        [Fact]
        public void BroadcastEvent_ForwardsToBroadcast()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("test-context");
            broadcastEvent.AddPayload(2, "test data");

            // Act
            _broadcastHandler?.Invoke(broadcastEvent);

            // Assert
            _socketServer.Verify(x => x.Broadcast(broadcastEvent), Times.Once);
        }

        #endregion

        #region 7.8 Dispose

        [Fact]
        public void Dispose_DisposesSocketServer()
        {
            // Act
            _sut.Dispose();

            // Assert
            _socketServer.Verify(x => x.Dispose(), Times.Once);
        }

        [Fact]
        public void Dispose_DisposesServiceDiscovery()
        {
            // Act
            _sut.Dispose();

            // Assert
            _serviceDiscovery.Verify(x => x.Dispose(), Times.Once);
        }

        #endregion
    }
}
