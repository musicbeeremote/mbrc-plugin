using FluentAssertions;
using MusicBeePlugin.Utilities.Common;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class StringCleanupExtensionTests
    {
        [Fact]
        public void Cleanup_RemovesLeadingWhitespace()
        {
            // Arrange
            var input = "   hello";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("hello");
        }

        [Fact]
        public void Cleanup_RemovesTrailingWhitespace()
        {
            // Arrange
            var input = "hello   ";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("hello");
        }

        [Fact]
        public void Cleanup_RemovesBothLeadingAndTrailingWhitespace()
        {
            // Arrange
            var input = "   hello   ";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("hello");
        }

        [Fact]
        public void Cleanup_RemovesTabCharacters()
        {
            // Arrange
            var input = "hello\tworld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_RemovesNewlineCharacters()
        {
            // Arrange
            var input = "hello\nworld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_RemovesCarriageReturnCharacters()
        {
            // Arrange
            var input = "hello\rworld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_RemovesCrLfSequence()
        {
            // Arrange
            var input = "hello\r\nworld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_PreservesInternalSpaces()
        {
            // Arrange
            var input = "hello world";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("hello world");
        }

        [Fact]
        public void Cleanup_PreservesMultipleInternalSpaces()
        {
            // Arrange
            var input = "hello   world";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("hello   world");
        }

        [Fact]
        public void Cleanup_RemovesNullCharacter()
        {
            // Arrange
            var input = "hello\0world";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_RemovesBellCharacter()
        {
            // Arrange
            var input = "hello\aworld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_RemovesBackspaceCharacter()
        {
            // Arrange
            var input = "hello\bworld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_RemovesFormFeedCharacter()
        {
            // Arrange
            var input = "hello\fworld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_RemovesVerticalTabCharacter()
        {
            // Arrange
            var input = "hello\vworld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Fact]
        public void Cleanup_HandlesEmptyString()
        {
            // Arrange
            var input = "";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void Cleanup_HandlesWhitespaceOnlyString()
        {
            // Arrange
            var input = "   ";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void Cleanup_PreservesUnicodeCharacters()
        {
            // Arrange
            var input = "héllo wörld";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("héllo wörld");
        }

        [Fact]
        public void Cleanup_PreservesNumbers()
        {
            // Arrange
            var input = "hello123world";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("hello123world");
        }

        [Fact]
        public void Cleanup_PreservesPunctuation()
        {
            // Arrange
            var input = "hello, world!";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("hello, world!");
        }

        [Fact]
        public void Cleanup_HandlesComplexString()
        {
            // Arrange
            var input = "  \thello\r\n\tworld\0  ";

            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be("helloworld");
        }

        [Theory]
        [InlineData("test", "test")]
        [InlineData("  test  ", "test")]
        [InlineData("te\tst", "test")]
        [InlineData("te\nst", "test")]
        [InlineData("te\rst", "test")]
        public void Cleanup_WorksWithVariousInputs(string input, string expected)
        {
            // Act
            var result = input.Cleanup();

            // Assert
            result.Should().Be(expected);
        }
    }
}
