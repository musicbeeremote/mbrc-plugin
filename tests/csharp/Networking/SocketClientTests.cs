using FluentAssertions;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Networking
{
    public class SocketClientTests
    {
        [Fact]
        public void Constructor_SetsConnectionId()
        {
            // Act
            var client = new SocketClient("test-connection-123");

            // Assert
            client.ConnectionId.Should().Be("test-connection-123");
        }

        [Fact]
        public void Constructor_InitializesPacketNumberToZero()
        {
            // Act
            var client = new SocketClient("client1");

            // Assert
            client.PacketNumber.Should().Be(0);
        }

        [Fact]
        public void Constructor_InitializesClientProtocolVersionToTwo()
        {
            // Act
            var client = new SocketClient("client1");

            // Assert
            client.ClientProtocolVersion.Should().Be(2);
        }

        [Fact]
        public void Constructor_InitializesClientPlatformToUnknown()
        {
            // Act
            var client = new SocketClient("client1");

            // Assert
            client.ClientPlatform.Should().Be(ClientOS.Unknown);
        }

        [Fact]
        public void Constructor_InitializesBroadcastsEnabledToTrue()
        {
            // Act
            var client = new SocketClient("client1");

            // Assert
            client.BroadcastsEnabled.Should().BeTrue();
        }

        [Fact]
        public void Constructor_InitializesAuthenticatedToFalse()
        {
            // Act
            var client = new SocketClient("client1");

            // Assert
            client.Authenticated.Should().BeFalse();
        }

        [Fact]
        public void IncreasePacketNumber_IncreasesCount()
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act
            client.IncreasePacketNumber();

            // Assert
            client.PacketNumber.Should().Be(1);
        }

        [Fact]
        public void IncreasePacketNumber_AfterOnePacket_NotAuthenticated()
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act
            client.IncreasePacketNumber();

            // Assert
            client.Authenticated.Should().BeFalse();
        }

        [Fact]
        public void IncreasePacketNumber_AfterTwoPackets_BecomesAuthenticated()
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act
            client.IncreasePacketNumber(); // PacketNumber = 1
            client.IncreasePacketNumber(); // PacketNumber = 2

            // Assert
            client.PacketNumber.Should().Be(2);
            client.Authenticated.Should().BeTrue();
        }

        [Fact]
        public void IncreasePacketNumber_StopsAtForty()
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act - increase 50 times
            for (var i = 0; i < 50; i++)
            {
                client.IncreasePacketNumber();
            }

            // Assert - should cap at 40
            client.PacketNumber.Should().Be(40);
        }

        [Fact]
        public void ClientId_CanBeSet()
        {
            // Arrange
            var client = new SocketClient("connection1");

            // Act
            client.ClientId = "my-client-id";

            // Assert
            client.ClientId.Should().Be("my-client-id");
        }

        [Fact]
        public void ClientProtocolVersion_CanBeSet()
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act
            client.ClientProtocolVersion = 4;

            // Assert
            client.ClientProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void ClientPlatform_CanBeSet()
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act
            client.ClientPlatform = ClientOS.Android;

            // Assert
            client.ClientPlatform.Should().Be(ClientOS.Android);
        }

        [Fact]
        public void BroadcastsEnabled_CanBeDisabled()
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act
            client.BroadcastsEnabled = false;

            // Assert
            client.BroadcastsEnabled.Should().BeFalse();
        }

        [Fact]
        public void Authenticated_CanBeSetManually()
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act
            client.Authenticated = true;

            // Assert
            client.Authenticated.Should().BeTrue();
        }

        [Fact]
        public void ToString_ContainsConnectionId()
        {
            // Arrange
            var client = new SocketClient("test-conn-456");

            // Act
            var result = client.ToString();

            // Assert
            result.Should().Contain("test-conn-456");
        }

        [Fact]
        public void ToString_ContainsPlatform()
        {
            // Arrange
            var client = new SocketClient("client1") { ClientPlatform = ClientOS.iOS };

            // Act
            var result = client.ToString();

            // Assert
            result.Should().Contain("iOS");
        }

        [Fact]
        public void ToString_ContainsProtocolVersion()
        {
            // Arrange
            var client = new SocketClient("client1") { ClientProtocolVersion = 4 };

            // Act
            var result = client.ToString();

            // Assert
            result.Should().Contain("Protocol=v4");
        }

        [Fact]
        public void ToString_ContainsAuthenticationStatus()
        {
            // Arrange
            var client = new SocketClient("client1");
            client.IncreasePacketNumber();
            client.IncreasePacketNumber();

            // Act
            var result = client.ToString();

            // Assert
            result.Should().Contain("Authenticated=True");
        }

        [Fact]
        public void ToString_ContainsBroadcastStatus()
        {
            // Arrange
            var client = new SocketClient("client1") { BroadcastsEnabled = false };

            // Act
            var result = client.ToString();

            // Assert
            result.Should().Contain("Broadcasts=False");
        }

        [Fact]
        public void ToString_ContainsPacketNumber()
        {
            // Arrange
            var client = new SocketClient("client1");
            client.IncreasePacketNumber();
            client.IncreasePacketNumber();
            client.IncreasePacketNumber();

            // Act
            var result = client.ToString();

            // Assert
            result.Should().Contain("Packets=3");
        }

        [Fact]
        public void ToString_IncludesClientId_WhenSet()
        {
            // Arrange
            var client = new SocketClient("connection1") { ClientId = "my-app-client" };

            // Act
            var result = client.ToString();

            // Assert
            result.Should().Contain("ClientId=my-app-client");
        }

        [Fact]
        public void ToString_ExcludesClientId_WhenNull()
        {
            // Arrange
            var client = new SocketClient("connection1");

            // Act
            var result = client.ToString();

            // Assert
            result.Should().NotContain("ClientId=");
        }

        [Fact]
        public void ToString_ExcludesClientId_WhenEmpty()
        {
            // Arrange
            var client = new SocketClient("connection1") { ClientId = "" };

            // Act
            var result = client.ToString();

            // Assert
            result.Should().NotContain("ClientId=");
        }

        [Theory]
        [InlineData(ClientOS.Android)]
        [InlineData(ClientOS.iOS)]
        [InlineData(ClientOS.Unknown)]
        public void ClientPlatform_AcceptsAllValues(ClientOS platform)
        {
            // Arrange
            var client = new SocketClient("client1");

            // Act
            client.ClientPlatform = platform;

            // Assert
            client.ClientPlatform.Should().Be(platform);
        }
    }
}
