using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MusicBeePlugin.Utilities.Common;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class IdGeneratorTests
    {
        private const string AllowedCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

        [Fact]
        public void GetUniqueKey_ReturnsStringOfLength8()
        {
            // Act
            var result = IdGenerator.GetUniqueKey();

            // Assert
            result.Should().HaveLength(8);
        }

        [Fact]
        public void GetUniqueKey_ReturnsOnlyAlphanumericCharacters()
        {
            // Act
            var result = IdGenerator.GetUniqueKey();

            // Assert
            result.Should().MatchRegex("^[a-zA-Z0-9]+$");
        }

        [Fact]
        public void GetUniqueKey_ContainsOnlyAllowedCharacters()
        {
            // Act
            var result = IdGenerator.GetUniqueKey();

            // Assert
            foreach (var c in result)
            {
                AllowedCharacters.Should().Contain(c.ToString());
            }
        }

        [Fact]
        public void GetUniqueKey_GeneratesUniqueKeys()
        {
            // Arrange
            var keys = new HashSet<string>();
            const int iterations = 100;

            // Act
            for (var i = 0; i < iterations; i++)
            {
                keys.Add(IdGenerator.GetUniqueKey());
            }

            // Assert - All keys should be unique
            keys.Should().HaveCount(iterations);
        }

        [Fact]
        public void GetUniqueKey_MultipleCallsReturnDifferentValues()
        {
            // Act
            var first = IdGenerator.GetUniqueKey();
            var second = IdGenerator.GetUniqueKey();

            // Assert
            first.Should().NotBe(second);
        }

        [Fact]
        public void GetUniqueKey_ReturnsNonEmptyString()
        {
            // Act
            var result = IdGenerator.GetUniqueKey();

            // Assert
            result.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void GetUniqueKey_DoesNotContainSpecialCharacters()
        {
            // Act
            var result = IdGenerator.GetUniqueKey();

            // Assert
            result.Should().NotContainAny("-", "_", "!", "@", "#", "$", "%", "^", "&", "*");
        }

        [Theory]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        public void GetUniqueKey_GeneratesDistinctKeys_ForMultipleIterations(int count)
        {
            // Act
            var keys = Enumerable.Range(0, count)
                .Select(_ => IdGenerator.GetUniqueKey())
                .ToList();

            // Assert
            keys.Distinct().Should().HaveCount(count);
        }
    }
}
