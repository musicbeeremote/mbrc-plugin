using FluentAssertions;
using MusicBeePlugin.Utilities.Network;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class ProtocolVersionMapperTests
    {
        #region TryParse - Integer Input

        [Theory]
        [InlineData(2, 2)]
        [InlineData(3, 3)]
        [InlineData(4, 4)]
        public void TryParse_IntegerValue_ReturnsCorrectVersion(int input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(expected);
        }

        [Fact]
        public void TryParse_IntegerBelowMin_ClampsToMin()
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(1, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(ProtocolVersionMapper.MinVersion);
        }

        [Fact]
        public void TryParse_IntegerAboveMax_ClampsToMax()
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(99, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(ProtocolVersionMapper.MaxVersion);
        }

        [Fact]
        public void TryParse_LongValue_ReturnsCorrectVersion()
        {
            // Arrange - JSON often deserializes integers as long
            long input = 3L;

            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(3);
        }

        #endregion

        #region TryParse - String Input

        [Theory]
        [InlineData("2", 2)]
        [InlineData("3", 3)]
        [InlineData("4", 4)]
        public void TryParse_StringInteger_ReturnsCorrectVersion(string input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(expected);
        }

        [Theory]
        [InlineData("2.1", 2)]  // Legacy 2.1 maps to V2 (integer part)
        [InlineData("2.2", 2)]  // Legacy 2.2 maps to V2 (integer part)
        [InlineData("2.5", 2)]  // Any 2.x maps to V2 (integer part)
        public void TryParse_LegacyFloatString_MapsToIntegerPart(string input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(expected);
        }

        [Fact]
        public void TryParse_StringFloat2Point0_MapsToV2()
        {
            // "2.0" is exactly V2, not a legacy float version
            var result = ProtocolVersionMapper.TryParse("2.0", out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(2);
        }

        [Theory]
        [InlineData("3.0", 3)]
        [InlineData("4.0", 4)]
        public void TryParse_StringFloatV3Plus_TruncatesToInt(string input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(expected);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void TryParse_EmptyOrNullString_ReturnsFalse(string input)
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeFalse();
            version.Should().Be(ProtocolVersionMapper.DefaultVersion);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("abc")]
        [InlineData("v3")]
        public void TryParse_InvalidString_ReturnsFalse(string input)
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region TryParse - Float/Double Input

        [Theory]
        [InlineData(2.1, 2)]
        [InlineData(2.2, 2)]
        [InlineData(2.9, 2)]
        public void TryParse_DoubleValue_LegacyFloat_MapsToIntegerPart(double input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(expected);
        }

        [Fact]
        public void TryParse_DoubleValue_2Point0_MapsToV2()
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(2.0, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(2);
        }

        [Theory]
        [InlineData(2.1f, 2)]
        [InlineData(2.2f, 2)]
        public void TryParse_FloatValue_LegacyFloat_MapsToIntegerPart(float input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(input, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(expected);
        }

        #endregion

        #region TryParse - Null Input

        [Fact]
        public void TryParse_NullValue_ReturnsFalse()
        {
            // Act
            var result = ProtocolVersionMapper.TryParse(null, out var version);

            // Assert
            result.Should().BeFalse();
            version.Should().Be(ProtocolVersionMapper.DefaultVersion);
        }

        #endregion

        #region TryParseString

        [Theory]
        [InlineData("2", 2)]
        [InlineData("3", 3)]
        [InlineData("4", 4)]
        [InlineData("2.1", 2)]
        [InlineData("2.2", 2)]
        public void TryParseString_ValidInput_ReturnsCorrectVersion(string input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.TryParseString(input, out var version);

            // Assert
            result.Should().BeTrue();
            version.Should().Be(expected);
        }

        #endregion

        #region MapLegacyFloat

        [Theory]
        [InlineData(2.0, 2)]   // 2.0 maps to V2
        [InlineData(2.1, 2)]   // 2.1 maps to V2 (integer part)
        [InlineData(2.2, 2)]   // 2.2 maps to V2 (integer part)
        [InlineData(2.5, 2)]   // 2.5 maps to V2 (integer part)
        [InlineData(2.99, 2)]  // 2.99 maps to V2 (integer part)
        [InlineData(3.0, 3)]   // 3.0 maps to V3
        [InlineData(4.0, 4)]   // 4.0 maps to V4
        public void MapLegacyFloat_ReturnsCorrectMapping(double input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.MapLegacyFloat(input);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void MapLegacyFloat_BelowMin_ClampsToMin()
        {
            // Act
            var result = ProtocolVersionMapper.MapLegacyFloat(1.5);

            // Assert
            result.Should().Be(ProtocolVersionMapper.MinVersion);
        }

        [Fact]
        public void MapLegacyFloat_AboveMax_ClampsToMax()
        {
            // Act
            var result = ProtocolVersionMapper.MapLegacyFloat(99.9);

            // Assert
            result.Should().Be(ProtocolVersionMapper.MaxVersion);
        }

        #endregion

        #region MapVersion

        [Theory]
        [InlineData(0, 2)]   // Below min clamps to min
        [InlineData(1, 2)]   // Below min clamps to min
        [InlineData(2, 2)]   // In range stays same
        [InlineData(3, 3)]   // In range stays same
        [InlineData(4, 4)]   // In range stays same
        [InlineData(5, 4)]   // Above max clamps to max
        [InlineData(99, 4)]  // Way above max clamps to max
        public void MapVersion_ClampsToSupportedRange(int input, int expected)
        {
            // Act
            var result = ProtocolVersionMapper.MapVersion(input);

            // Assert
            result.Should().Be(expected);
        }

        #endregion

        #region Constants

        [Fact]
        public void Constants_HaveCorrectValues()
        {
            ProtocolVersionMapper.MinVersion.Should().Be(2);
            ProtocolVersionMapper.MaxVersion.Should().Be(4);
            ProtocolVersionMapper.DefaultVersion.Should().Be(2);
        }

        #endregion

        #region Integration Scenarios

        [Fact]
        public void TryParse_AndroidClient_V21_MapsToV2()
        {
            // Scenario: Old Android client sends "2.1" as protocol version
            // Maps to integer part (2), capabilities handled separately
            var result = ProtocolVersionMapper.TryParse("2.1", out var version);

            result.Should().BeTrue();
            version.Should().Be(2);
        }

        [Fact]
        public void TryParse_NewClient_V4Object_MapsCorrectly()
        {
            // Scenario: New client sends 4 as integer
            var result = ProtocolVersionMapper.TryParse(4, out var version);

            result.Should().BeTrue();
            version.Should().Be(4);
        }

        [Fact]
        public void TryParse_FutureClient_V5_ClampsToV4()
        {
            // Scenario: Future client sends V5 (not yet supported)
            var result = ProtocolVersionMapper.TryParse(5, out var version);

            result.Should().BeTrue();
            version.Should().Be(4); // Clamped to max supported
        }

        #endregion
    }
}
