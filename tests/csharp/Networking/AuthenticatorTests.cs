using System;
using FluentAssertions;
using MusicBeePlugin.Utilities.Network;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Networking
{
    public class AuthenticatorTests
    {
        private readonly Authenticator _sut;

        public AuthenticatorTests()
        {
            _sut = new Authenticator();
        }

        [Fact]
        public void AddClientOnConnect_CreatesNewClient()
        {
            // Act
            _sut.AddClientOnConnect("client1");

            // Assert
            var client = _sut.Client("client1");
            client.Should().NotBeNull();
            client.ConnectionId.Should().Be("client1");
        }

        [Fact]
        public void AddClientOnConnect_ReplacesExistingClient()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");
            var firstClient = _sut.Client("client1");
            firstClient.IncreasePacketNumber();
            firstClient.IncreasePacketNumber();

            // Act
            _sut.AddClientOnConnect("client1");

            // Assert
            var newClient = _sut.Client("client1");
            newClient.PacketNumber.Should().Be(0); // Fresh client
            newClient.Authenticated.Should().BeFalse();
        }

        [Fact]
        public void Client_ReturnsNull_WhenClientDoesNotExist()
        {
            // Act
            var client = _sut.Client("nonexistent");

            // Assert
            client.Should().BeNull();
        }

        [Fact]
        public void Client_ReturnsClient_WhenExists()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");

            // Act
            var client = _sut.Client("client1");

            // Assert
            client.Should().NotBeNull();
        }

        [Fact]
        public void RemoveClientOnDisconnect_RemovesClient()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");

            // Act
            _sut.RemoveClientOnDisconnect("client1");

            // Assert
            _sut.Client("client1").Should().BeNull();
        }

        [Fact]
        public void RemoveClientOnDisconnect_DoesNotThrow_WhenClientDoesNotExist()
        {
            // Act
            Action act = () => _sut.RemoveClientOnDisconnect("nonexistent");

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void IsClientAuthenticated_ReturnsFalse_WhenClientDoesNotExist()
        {
            // Act
            var result = _sut.IsClientAuthenticated("nonexistent");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsClientAuthenticated_ReturnsFalse_WhenClientNotAuthenticated()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");

            // Act
            var result = _sut.IsClientAuthenticated("client1");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsClientAuthenticated_ReturnsTrue_WhenClientAuthenticated()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");
            var client = _sut.Client("client1");
            client.IncreasePacketNumber();
            client.IncreasePacketNumber();

            // Act
            var result = _sut.IsClientAuthenticated("client1");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsClientBroadcastEnabled_ReturnsTrue_WhenClientDoesNotExist()
        {
            // Note: This is the documented behavior - non-existent clients default to broadcast enabled
            // Act
            var result = _sut.IsClientBroadcastEnabled("nonexistent");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsClientBroadcastEnabled_ReturnsTrue_WhenBroadcastsEnabled()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");

            // Act
            var result = _sut.IsClientBroadcastEnabled("client1");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsClientBroadcastEnabled_ReturnsFalse_WhenBroadcastsDisabled()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");
            _sut.Client("client1").BroadcastsEnabled = false;

            // Act
            var result = _sut.IsClientBroadcastEnabled("client1");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void MultipleClients_AreTrackedIndependently()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");
            _sut.AddClientOnConnect("client2");
            _sut.AddClientOnConnect("client3");

            _sut.Client("client1").ClientProtocolVersion = 2;
            _sut.Client("client2").ClientProtocolVersion = 3;
            _sut.Client("client3").ClientProtocolVersion = 4;

            // Act & Assert
            _sut.Client("client1").ClientProtocolVersion.Should().Be(2);
            _sut.Client("client2").ClientProtocolVersion.Should().Be(3);
            _sut.Client("client3").ClientProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void MultipleClients_AuthenticateIndependently()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");
            _sut.AddClientOnConnect("client2");

            var client1 = _sut.Client("client1");
            client1.IncreasePacketNumber();
            client1.IncreasePacketNumber();

            // Act & Assert
            _sut.IsClientAuthenticated("client1").Should().BeTrue();
            _sut.IsClientAuthenticated("client2").Should().BeFalse();
        }

        [Fact]
        public void RemoveClient_DoesNotAffectOtherClients()
        {
            // Arrange
            _sut.AddClientOnConnect("client1");
            _sut.AddClientOnConnect("client2");

            // Act
            _sut.RemoveClientOnDisconnect("client1");

            // Assert
            _sut.Client("client1").Should().BeNull();
            _sut.Client("client2").Should().NotBeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("client-with-dashes")]
        [InlineData("client_with_underscores")]
        [InlineData("client.with.dots")]
        [InlineData("192.168.1.1:12345")]
        public void AddClientOnConnect_AcceptsVariousClientIdFormats(string clientId)
        {
            // Act
            _sut.AddClientOnConnect(clientId);

            // Assert
            _sut.Client(clientId).Should().NotBeNull();
        }
    }
}
