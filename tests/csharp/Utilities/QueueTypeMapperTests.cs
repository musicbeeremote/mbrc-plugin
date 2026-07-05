using FluentAssertions;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Utilities.Mapping;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class QueueTypeMapperTests
    {
        [Fact]
        public void MapFromString_ReturnsNext_ForNextString()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("next");

            // Assert
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_ReturnsLast_ForLastString()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("last");

            // Assert
            result.Should().Be(QueueType.Last);
        }

        [Fact]
        public void MapFromString_ReturnsPlayNow_ForNowString()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("now");

            // Assert
            result.Should().Be(QueueType.PlayNow);
        }

        [Fact]
        public void MapFromString_ReturnsAddAndPlay_ForAddAllString()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("add-all");

            // Assert
            result.Should().Be(QueueType.AddAndPlay);
        }

        [Fact]
        public void MapFromString_ReturnsNext_ForUnknownString()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("unknown");

            // Assert
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_ReturnsNext_ForEmptyString()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("");

            // Assert
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_ReturnsNext_ForNullString()
        {
            // Act
            var result = QueueTypeMapper.MapFromString(null);

            // Assert
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_IsCaseSensitive_UppercaseNext()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("Next");

            // Assert - uppercase doesn't match, returns default
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_IsCaseSensitive_UppercaseLast()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("Last");

            // Assert - uppercase doesn't match, returns default
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_IsCaseSensitive_UppercaseNow()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("Now");

            // Assert - uppercase doesn't match, returns default
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_IsCaseSensitive_UppercaseAddAll()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("Add-All");

            // Assert - uppercase doesn't match, returns default
            result.Should().Be(QueueType.Next);
        }

        [Theory]
        [InlineData("next", QueueType.Next)]
        [InlineData("last", QueueType.Last)]
        [InlineData("now", QueueType.PlayNow)]
        [InlineData("add-all", QueueType.AddAndPlay)]
        public void MapFromString_MapsKnownStringsCorrectly(string input, QueueType expected)
        {
            // Act
            var result = QueueTypeMapper.MapFromString(input);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("random")]
        [InlineData("queue")]
        [InlineData("play")]
        [InlineData("first")]
        [InlineData("  next")]  // with whitespace
        [InlineData("next  ")]  // with trailing whitespace
        public void MapFromString_ReturnsNext_ForInvalidStrings(string input)
        {
            // Act
            var result = QueueTypeMapper.MapFromString(input);

            // Assert
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_ReturnsNext_ForWhitespaceString()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("   ");

            // Assert
            result.Should().Be(QueueType.Next);
        }

        [Fact]
        public void MapFromString_ReturnsNext_ForMixedCaseStrings()
        {
            // Act
            var result = QueueTypeMapper.MapFromString("NeXt");

            // Assert - mixed case doesn't match exact string
            result.Should().Be(QueueType.Next);
        }
    }
}
