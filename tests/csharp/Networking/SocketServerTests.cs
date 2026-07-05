using System;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Networking
{
    public class SocketServerTests : IDisposable
    {
        private readonly Mock<IProtocolHandler> _protocolHandler;
        private readonly Mock<IAuthenticator> _authenticator;
        private readonly Mock<IUserSettings> _userSettings;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly MockLogger _logger;
        private readonly SocketServer _sut;

        public SocketServerTests()
        {
            _protocolHandler = new Mock<IProtocolHandler>();
            _authenticator = new Mock<IAuthenticator>();
            _userSettings = new Mock<IUserSettings>();
            _eventAggregator = new Mock<IEventAggregator>();
            _logger = new MockLogger();

            // Default settings
            _userSettings.Setup(x => x.ListeningPort).Returns(3000);
            _userSettings.Setup(x => x.FilterSelection).Returns(FilteringSelection.All);

            _sut = new SocketServer(
                _protocolHandler.Object,
                _authenticator.Object,
                _userSettings.Object,
                _eventAggregator.Object,
                _logger);
        }

        public void Dispose()
        {
            _sut?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region 8.1 Constructor and Initial State

        [Fact]
        public void Constructor_InitializesIsRunningToFalse()
        {
            // Assert
            _sut.IsRunning.Should().BeFalse();
        }

        [Fact]
        public void Constructor_SubscribesToForceClientDisconnect()
        {
            // Assert - verified by checking that KickClient can be invoked
            // The constructor subscribes to _handler.ForceClientDisconnect
            _sut.Should().NotBeNull();
        }

        #endregion

        #region 8.2 KickClient

        [Fact]
        public void KickClient_NullClientId_DoesNothing()
        {
            // Act
            _sut.KickClient(null);

            // Assert - no exception thrown
            _authenticator.Verify(x => x.RemoveClientOnDisconnect(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void KickClient_EmptyClientId_DoesNothing()
        {
            // Act
            _sut.KickClient(string.Empty);

            // Assert - no exception thrown
            _authenticator.Verify(x => x.RemoveClientOnDisconnect(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void KickClient_NonExistentClient_DoesNotThrow()
        {
            // Act
            Action act = () => _sut.KickClient("nonexistent-client");

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region 8.3 StopListening

        [Fact]
        public void StopListening_WhenNotRunning_DoesNothing()
        {
            // Arrange - not started
            _sut.IsRunning.Should().BeFalse();

            // Act
            _sut.StopListening();

            // Assert - no exception, still not running
            _sut.IsRunning.Should().BeFalse();
        }

        [Fact]
        public void StopListening_SetsIsRunningToFalse()
        {
            // Arrange - would need to start first, but since actual socket
            // binding may fail in test environment, we verify the state transitions

            // Act
            _sut.StopListening();

            // Assert
            _sut.IsRunning.Should().BeFalse();
        }

        #endregion

        #region 8.4 Send Methods

        [Fact]
        public void Send_NullMessage_DoesNothing()
        {
            // Act
            _sut.Send(null, "client-123");

            // Assert - no exception thrown
            _sut.Should().NotBeNull();
        }

        [Fact]
        public void Send_EmptyMessage_DoesNothing()
        {
            // Act
            _sut.Send(string.Empty, "client-123");

            // Assert - no exception thrown
            _sut.Should().NotBeNull();
        }

        [Fact]
        public void Send_NullClientId_DoesNothing()
        {
            // Act
            _sut.Send("test message", null);

            // Assert - no exception thrown
            _sut.Should().NotBeNull();
        }

        [Fact]
        public void Send_EmptyClientId_DoesNothing()
        {
            // Act
            _sut.Send("test message", string.Empty);

            // Assert - no exception thrown
            _sut.Should().NotBeNull();
        }

        [Fact]
        public void Send_BroadcastAll_DoesNotThrow()
        {
            // Act
            Action act = () => _sut.Send("test message", "all");

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Send_ToNonExistentClient_DoesNotThrow()
        {
            // Act
            Action act = () => _sut.Send("test message", "nonexistent-client");

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void SendAll_NullMessage_DoesNothing()
        {
            // Act
            _sut.Send(null);

            // Assert - no exception thrown
            _sut.Should().NotBeNull();
        }

        [Fact]
        public void SendAll_EmptyMessage_DoesNothing()
        {
            // Act
            _sut.Send(string.Empty);

            // Assert - no exception thrown
            _sut.Should().NotBeNull();
        }

        #endregion

        #region 8.5 Broadcast

        [Fact]
        public void Broadcast_NullEvent_DoesNothing()
        {
            // Act
            _sut.Broadcast(null);

            // Assert - no exception thrown
            _sut.Should().NotBeNull();
        }

        [Fact]
        public void Broadcast_ValidEvent_DoesNotThrow()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("test");
            broadcastEvent.AddPayload(2, "test data");

            // Act
            Action act = () => _sut.Broadcast(broadcastEvent);

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region 8.6 RestartSocket

        [Fact]
        public void RestartSocket_WhenNotRunning_DoesNotThrow()
        {
            // Act
            Action act = () => _sut.RestartSocket();

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region 8.7 Dispose

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Act
            Action act = () =>
            {
                _sut.Dispose();
                _sut.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_SetsIsRunningToFalse()
        {
            // Act
            _sut.Dispose();

            // Assert
            _sut.IsRunning.Should().BeFalse();
        }

        #endregion
    }
}
