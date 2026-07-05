using System;
using System.Net;
using FluentAssertions;
using MusicBeePlugin.Tools;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class NetworkToolsTests
    {
        #region GetNetworkAddress Tests

        [Fact]
        public void GetNetworkAddress_CalculatesCorrectly_ForClassCNetwork()
        {
            // Arrange
            var address = IPAddress.Parse("192.168.1.100");
            var subnetMask = IPAddress.Parse("255.255.255.0");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("192.168.1.0");
        }

        [Fact]
        public void GetNetworkAddress_CalculatesCorrectly_ForClassBNetwork()
        {
            // Arrange
            var address = IPAddress.Parse("172.16.50.100");
            var subnetMask = IPAddress.Parse("255.255.0.0");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("172.16.0.0");
        }

        [Fact]
        public void GetNetworkAddress_CalculatesCorrectly_ForClassANetwork()
        {
            // Arrange
            var address = IPAddress.Parse("10.50.100.150");
            var subnetMask = IPAddress.Parse("255.0.0.0");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("10.0.0.0");
        }

        [Fact]
        public void GetNetworkAddress_CalculatesCorrectly_ForSlash24Subnet()
        {
            // Arrange
            var address = IPAddress.Parse("10.0.0.1");
            var subnetMask = IPAddress.Parse("255.255.255.0");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("10.0.0.0");
        }

        [Fact]
        public void GetNetworkAddress_CalculatesCorrectly_ForSlash25Subnet()
        {
            // Arrange - /25 subnet splits at .128
            var address = IPAddress.Parse("192.168.1.200");
            var subnetMask = IPAddress.Parse("255.255.255.128");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("192.168.1.128");
        }

        [Fact]
        public void GetNetworkAddress_CalculatesCorrectly_ForSlash30Subnet()
        {
            // Arrange - /30 subnet (only 2 usable addresses)
            var address = IPAddress.Parse("192.168.1.5");
            var subnetMask = IPAddress.Parse("255.255.255.252");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("192.168.1.4");
        }

        [Fact]
        public void GetNetworkAddress_ReturnsZeroAddress_ForAllZerosMask()
        {
            // Arrange
            var address = IPAddress.Parse("192.168.1.100");
            var subnetMask = IPAddress.Parse("0.0.0.0");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("0.0.0.0");
        }

        [Fact]
        public void GetNetworkAddress_ReturnsSameAddress_ForAllOnesMask()
        {
            // Arrange
            var address = IPAddress.Parse("192.168.1.100");
            var subnetMask = IPAddress.Parse("255.255.255.255");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("192.168.1.100");
        }

        [Fact]
        public void GetNetworkAddress_ThrowsArgumentException_WhenLengthsDontMatch()
        {
            // Arrange
            var address = IPAddress.Parse("192.168.1.100");
            var subnetMask = IPAddress.IPv6Loopback; // IPv6 has 16 bytes, IPv4 has 4

            // Act
            Action act = () => NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("ip and mask lengths don't match");
        }

        [Theory]
        [InlineData("192.168.1.1", "255.255.255.0", "192.168.1.0")]
        [InlineData("192.168.1.255", "255.255.255.0", "192.168.1.0")]
        [InlineData("10.20.30.40", "255.255.0.0", "10.20.0.0")]
        [InlineData("172.16.254.100", "255.255.255.128", "172.16.254.0")]
        public void GetNetworkAddress_CalculatesCorrectly_ForVariousInputs(
            string addressStr, string maskStr, string expectedStr)
        {
            // Arrange
            var address = IPAddress.Parse(addressStr);
            var subnetMask = IPAddress.Parse(maskStr);

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be(expectedStr);
        }

        [Fact]
        public void GetNetworkAddress_HandlesLoopbackAddress()
        {
            // Arrange
            var address = IPAddress.Parse("127.0.0.1");
            var subnetMask = IPAddress.Parse("255.0.0.0");

            // Act
            var result = NetworkTools.GetNetworkAddress(address, subnetMask);

            // Assert
            result.ToString().Should().Be("127.0.0.0");
        }

        #endregion

        #region GetPrivateAddressList Tests

        [Fact]
        public void GetPrivateAddressList_ReturnsNonNullList()
        {
            // Act
            var result = NetworkTools.GetPrivateAddressList();

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void GetPrivateAddressList_ReturnsListOfStrings()
        {
            // Act
            var result = NetworkTools.GetPrivateAddressList();

            // Assert
            result.Should().AllSatisfy(ip => ip.Should().NotBeNullOrEmpty());
        }

        [Fact]
        public void GetPrivateAddressList_ContainsValidIpAddresses()
        {
            // Act
            var result = NetworkTools.GetPrivateAddressList();

            // Assert
            foreach (var ip in result)
            {
                IPAddress.TryParse(ip, out var parsed).Should().BeTrue($"'{ip}' should be valid IP");
                parsed.AddressFamily.Should().Be(System.Net.Sockets.AddressFamily.InterNetwork);
            }
        }

        #endregion

        #region GetAddressList Tests

        [Fact]
        public void GetAddressList_ReturnsNonNullList()
        {
            // Act
            var result = NetworkTools.GetAddressList();

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void GetAddressList_ReturnsListOfIPAddresses()
        {
            // Act
            var result = NetworkTools.GetAddressList();

            // Assert
            result.Should().AllSatisfy(ip => ip.Should().NotBeNull());
        }

        [Fact]
        public void GetAddressList_ContainsOnlyIPv4Addresses()
        {
            // Act
            var result = NetworkTools.GetAddressList();

            // Assert
            result.Should().AllSatisfy(ip =>
                ip.AddressFamily.Should().Be(System.Net.Sockets.AddressFamily.InterNetwork));
        }

        [Fact]
        public void GetAddressList_ResultsMatchPrivateAddressList()
        {
            // Act
            var ipAddresses = NetworkTools.GetAddressList();
            var stringAddresses = NetworkTools.GetPrivateAddressList();

            // Assert
            ipAddresses.Should().HaveCount(stringAddresses.Count);
            for (int i = 0; i < ipAddresses.Count; i++)
            {
                ipAddresses[i].ToString().Should().Be(stringAddresses[i]);
            }
        }

        #endregion

        #region GetSubnetMask Tests

        [Fact]
        public void GetSubnetMask_ThrowsArgumentException_ForNonExistentAddress()
        {
            // Arrange - Use an address that almost certainly won't be on the system
            var nonExistentAddress = "203.0.113.50"; // TEST-NET-3 reserved range

            // Act
            Action act = () => NetworkTools.GetSubnetMask(nonExistentAddress);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("*unable to find subnet mask*");
        }

        [Fact]
        public void GetSubnetMask_ThrowsFormatException_ForInvalidAddress()
        {
            // Arrange
            var invalidAddress = "not.an.ip.address";

            // Act
            Action act = () => NetworkTools.GetSubnetMask(invalidAddress);

            // Assert
            act.Should().Throw<FormatException>();
        }

        [Fact]
        public void GetSubnetMask_ThrowsFormatException_ForEmptyString()
        {
            // Act
            Action act = () => NetworkTools.GetSubnetMask("");

            // Assert
            act.Should().Throw<FormatException>();
        }

        #endregion
    }
}
