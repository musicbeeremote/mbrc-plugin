using FluentAssertions;
using Moq;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Utilities.Network;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Networking
{
    public class ProtocolCapabilitiesTests
    {
        private readonly Mock<IAuthenticator> _authenticator;
        private readonly ProtocolCapabilities _sut;

        public ProtocolCapabilitiesTests()
        {
            _authenticator = new Mock<IAuthenticator>();
            _sut = new ProtocolCapabilities(_authenticator.Object);
        }

        private void SetupClientWithVersion(string clientId, int version, ClientOS platform = ClientOS.Unknown)
        {
            SetupClientWithVersion(clientId, (double)version, platform);
        }

        private void SetupClientWithVersion(string clientId, double rawVersion, ClientOS platform = ClientOS.Unknown)
        {
            var mappedVersion = ProtocolVersionMapper.MapVersion((int)rawVersion);
            var client = new SocketClient(clientId)
            {
                ClientProtocolVersion = mappedVersion,
                ClientPlatform = platform
            };
            // Set capabilities based on raw version (preserves 2.1, 2.2 feature support)
            client.SetCapabilitiesFromVersion(rawVersion);

            _authenticator.Setup(x => x.Client(clientId)).Returns(client);
        }

        [Fact]
        public void GetClientPlatform_ReturnsPlatformFromClient()
        {
            // Arrange
            var client = new SocketClient("client1") { ClientPlatform = ClientOS.Android };
            _authenticator.Setup(x => x.Client("client1")).Returns(client);

            // Act
            var result = _sut.GetClientPlatform("client1");

            // Assert
            result.Should().Be(ClientOS.Android);
        }

        [Fact]
        public void GetClientPlatform_ReturnsUnknown_WhenClientIsNull()
        {
            // Arrange
            _authenticator.Setup(x => x.Client("client1")).Returns((SocketClient)null);

            // Act
            var result = _sut.GetClientPlatform("client1");

            // Assert
            result.Should().Be(ClientOS.Unknown);
        }

        [Theory]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, true)]
        [InlineData(5, true)]
        public void SupportsPayloadObjects_BasedOnVersion(int version, bool expected)
        {
            // Arrange
            SetupClientWithVersion("client1", version);

            // Act
            var result = _sut.SupportsPayloadObjects("client1");

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, true)]
        [InlineData(5, true)]
        public void SupportsPagination_BasedOnVersion(int version, bool expected)
        {
            // Arrange
            SetupClientWithVersion("client1", version);

            // Act
            var result = _sut.SupportsPagination("client1");

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, true)]
        [InlineData(5, true)]
        public void SupportsAutoDjShuffle_BasedOnVersion(int version, bool expected)
        {
            // Arrange
            SetupClientWithVersion("client1", version);

            // Act
            var result = _sut.SupportsAutoDjShuffle("client1");

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, true)]
        [InlineData(5, true)]
        public void SupportsFullPlayerStatus_BasedOnVersion(int version, bool expected)
        {
            // Arrange
            SetupClientWithVersion("client1", version);

            // Act
            var result = _sut.SupportsFullPlayerStatus("client1");

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void AllV3Features_NotSupportedForV2Client()
        {
            // Arrange
            SetupClientWithVersion("v2client", 2);

            // Act & Assert
            _sut.SupportsPayloadObjects("v2client").Should().BeFalse();
            _sut.SupportsPagination("v2client").Should().BeFalse();
            _sut.SupportsAutoDjShuffle("v2client").Should().BeFalse();
            _sut.SupportsFullPlayerStatus("v2client").Should().BeFalse();
        }

        [Fact]
        public void AllV3Features_SupportedForV3Client()
        {
            // Arrange
            SetupClientWithVersion("v3client", 3);

            // Act & Assert
            _sut.SupportsPayloadObjects("v3client").Should().BeTrue();
            _sut.SupportsPagination("v3client").Should().BeTrue();
            _sut.SupportsAutoDjShuffle("v3client").Should().BeTrue();
            _sut.SupportsFullPlayerStatus("v3client").Should().BeTrue();
        }

        [Fact]
        public void AllV3Features_SupportedForV4Client()
        {
            // Arrange
            SetupClientWithVersion("v4client", 4);

            // Act & Assert
            _sut.SupportsPayloadObjects("v4client").Should().BeTrue();
            _sut.SupportsPagination("v4client").Should().BeTrue();
            _sut.SupportsAutoDjShuffle("v4client").Should().BeTrue();
            _sut.SupportsFullPlayerStatus("v4client").Should().BeTrue();
        }

        [Fact]
        public void DifferentClients_HaveDifferentCapabilities()
        {
            // Arrange
            SetupClientWithVersion("oldClient", 2);
            SetupClientWithVersion("newClient", 4);

            // Act & Assert
            _sut.SupportsPagination("oldClient").Should().BeFalse();
            _sut.SupportsPagination("newClient").Should().BeTrue();
        }

        [Theory]
        [InlineData(ClientOS.Android)]
        [InlineData(ClientOS.iOS)]
        [InlineData(ClientOS.Unknown)]
        public void GetClientPlatform_ReturnsCorrectPlatform(ClientOS platform)
        {
            // Arrange
            var client = new SocketClient("client1") { ClientPlatform = platform };
            _authenticator.Setup(x => x.Client("client1")).Returns(client);

            // Act
            var result = _sut.GetClientPlatform("client1");

            // Assert
            result.Should().Be(platform);
        }

    }
}
