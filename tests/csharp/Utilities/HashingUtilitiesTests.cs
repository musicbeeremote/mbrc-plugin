using System;
using System.IO;
using System.Text;
using FluentAssertions;
using MusicBeePlugin.Utilities.Common;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class HashingUtilitiesTests
    {
        private const int Sha1HashLength = 40;

        #region Sha1Hash - String Input

        [Fact]
        public void Sha1Hash_NullString_ReturnsEmptyHash()
        {
            // Act
            var result = HashingUtilities.Sha1Hash((string)null);

            // Assert
            result.Should().HaveLength(Sha1HashLength);
            result.Should().Be(HashingUtilities.EmptyHash);
        }

        [Fact]
        public void Sha1Hash_EmptyString_ReturnsEmptyHash()
        {
            // Act
            var result = HashingUtilities.Sha1Hash(string.Empty);

            // Assert
            result.Should().Be(HashingUtilities.EmptyHash);
        }

        [Fact]
        public void Sha1Hash_ValidString_ReturnsCorrectHash()
        {
            // Arrange - "hello" has a known SHA1 hash
            const string input = "hello";
            const string expectedHash = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";

            // Act
            var result = HashingUtilities.Sha1Hash(input);

            // Assert
            result.Should().Be(expectedHash);
        }

        [Fact]
        public void Sha1Hash_SameInput_ReturnsSameHash()
        {
            // Arrange
            const string input = "test string";

            // Act
            var hash1 = HashingUtilities.Sha1Hash(input);
            var hash2 = HashingUtilities.Sha1Hash(input);

            // Assert
            hash1.Should().Be(hash2);
        }

        [Fact]
        public void Sha1Hash_DifferentInputs_ReturnsDifferentHashes()
        {
            // Arrange
            const string input1 = "string1";
            const string input2 = "string2";

            // Act
            var hash1 = HashingUtilities.Sha1Hash(input1);
            var hash2 = HashingUtilities.Sha1Hash(input2);

            // Assert
            hash1.Should().NotBe(hash2);
        }

        [Fact]
        public void Sha1Hash_ReturnsLowercaseHex()
        {
            // Act
            var result = HashingUtilities.Sha1Hash("test");

            // Assert
            result.Should().MatchRegex("^[a-f0-9]{40}$");
        }

        [Fact]
        public void Sha1Hash_UnicodeString_Works()
        {
            // Arrange
            const string input = "日本語テスト";

            // Act
            var result = HashingUtilities.Sha1Hash(input);

            // Assert
            result.Should().HaveLength(Sha1HashLength);
            result.Should().NotBe(HashingUtilities.EmptyHash);
        }

        #endregion

        #region Sha1Hash - Byte Array Input

        [Fact]
        public void Sha1Hash_NullByteArray_ReturnsEmptyHash()
        {
            // Act
            var result = HashingUtilities.Sha1Hash((byte[])null);

            // Assert
            result.Should().Be(HashingUtilities.EmptyHash);
        }

        [Fact]
        public void Sha1Hash_EmptyByteArray_ReturnsEmptyHash()
        {
            // Act
            var result = HashingUtilities.Sha1Hash(Array.Empty<byte>());

            // Assert
            result.Should().Be(HashingUtilities.EmptyHash);
        }

        [Fact]
        public void Sha1Hash_ByteArray_ReturnsCorrectHash()
        {
            // Arrange - UTF8 bytes of "hello"
            var input = Encoding.UTF8.GetBytes("hello");
            const string expectedHash = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";

            // Act
            var result = HashingUtilities.Sha1Hash(input);

            // Assert
            result.Should().Be(expectedHash);
        }

        [Fact]
        public void Sha1Hash_StringAndBytes_ProduceSameHash()
        {
            // Arrange
            const string stringInput = "test data";
            var byteInput = Encoding.UTF8.GetBytes(stringInput);

            // Act
            var stringHash = HashingUtilities.Sha1Hash(stringInput);
            var byteHash = HashingUtilities.Sha1Hash(byteInput);

            // Assert
            stringHash.Should().Be(byteHash);
        }

        [Fact]
        public void Sha1Hash_BinaryData_Works()
        {
            // Arrange - non-text binary data
            var binaryData = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD };

            // Act
            var result = HashingUtilities.Sha1Hash(binaryData);

            // Assert
            result.Should().HaveLength(Sha1HashLength);
            result.Should().NotBe(HashingUtilities.EmptyHash);
        }

        #endregion

        #region Sha1Hash - Stream Input

        [Fact]
        public void Sha1Hash_NullStream_ReturnsEmptyHash()
        {
            // Act
            var result = HashingUtilities.Sha1Hash((Stream)null);

            // Assert
            result.Should().Be(HashingUtilities.EmptyHash);
        }

        [Fact]
        public void Sha1Hash_EmptyStream_ReturnsKnownHash()
        {
            // Arrange - empty input has a specific SHA1 hash
            const string expectedHash = "da39a3ee5e6b4b0d3255bfef95601890afd80709";

            using (var stream = new MemoryStream())
            {
                // Act
                var result = HashingUtilities.Sha1Hash(stream);

                // Assert
                result.Should().Be(expectedHash);
            }
        }

        [Fact]
        public void Sha1Hash_StreamWithData_ReturnsCorrectHash()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("hello");
            const string expectedHash = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";

            using (var stream = new MemoryStream(data))
            {
                // Act
                var result = HashingUtilities.Sha1Hash(stream);

                // Assert
                result.Should().Be(expectedHash);
            }
        }

        [Fact]
        public void Sha1Hash_StreamAndBytes_ProduceSameHash()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("test stream data");

            using (var stream = new MemoryStream(data))
            {
                // Act
                var streamHash = HashingUtilities.Sha1Hash(stream);
                var byteHash = HashingUtilities.Sha1Hash(data);

                // Assert
                streamHash.Should().Be(byteHash);
            }
        }

        #endregion

        #region Sha1HashFile

        [Fact]
        public void Sha1HashFile_ExistingFile_ReturnsCorrectHash()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "hello");
                const string expectedHash = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";

                // Act
                var result = HashingUtilities.Sha1HashFile(tempFile);

                // Assert
                result.Should().Be(expectedHash);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void Sha1HashFile_EmptyFile_ReturnsKnownHash()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            try
            {
                // File is already empty
                const string expectedHash = "da39a3ee5e6b4b0d3255bfef95601890afd80709";

                // Act
                var result = HashingUtilities.Sha1HashFile(tempFile);

                // Assert
                result.Should().Be(expectedHash);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void Sha1HashFile_BinaryFile_Works()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            try
            {
                var binaryData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                File.WriteAllBytes(tempFile, binaryData);

                // Act
                var result = HashingUtilities.Sha1HashFile(tempFile);

                // Assert
                result.Should().HaveLength(Sha1HashLength);
                result.Should().NotBe(HashingUtilities.EmptyHash);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        #endregion

        #region CoverIdentifier

        [Fact]
        public void CoverIdentifier_ValidInput_ReturnsHash()
        {
            // Arrange
            const string artist = "The Beatles";
            const string album = "Abbey Road";

            // Act
            var result = HashingUtilities.CoverIdentifier(artist, album);

            // Assert
            result.Should().HaveLength(Sha1HashLength);
            result.Should().NotBe(HashingUtilities.EmptyHash);
        }

        [Fact]
        public void CoverIdentifier_SameInput_ReturnsSameHash()
        {
            // Arrange
            const string artist = "Artist Name";
            const string album = "Album Title";

            // Act
            var hash1 = HashingUtilities.CoverIdentifier(artist, album);
            var hash2 = HashingUtilities.CoverIdentifier(artist, album);

            // Assert
            hash1.Should().Be(hash2);
        }

        [Fact]
        public void CoverIdentifier_CaseInsensitive()
        {
            // Arrange
            const string artist = "The Beatles";
            const string album = "Abbey Road";

            // Act
            var hash1 = HashingUtilities.CoverIdentifier(artist, album);
            var hash2 = HashingUtilities.CoverIdentifier(artist.ToUpperInvariant(), album.ToUpperInvariant());

            // Assert - case should not matter
            hash1.Should().Be(hash2);
        }

        [Fact]
        public void CoverIdentifier_DifferentArtist_ReturnsDifferentHash()
        {
            // Arrange
            const string album = "Same Album";

            // Act
            var hash1 = HashingUtilities.CoverIdentifier("Artist 1", album);
            var hash2 = HashingUtilities.CoverIdentifier("Artist 2", album);

            // Assert
            hash1.Should().NotBe(hash2);
        }

        [Fact]
        public void CoverIdentifier_DifferentAlbum_ReturnsDifferentHash()
        {
            // Arrange
            const string artist = "Same Artist";

            // Act
            var hash1 = HashingUtilities.CoverIdentifier(artist, "Album 1");
            var hash2 = HashingUtilities.CoverIdentifier(artist, "Album 2");

            // Assert
            hash1.Should().NotBe(hash2);
        }

        [Fact]
        public void CoverIdentifier_OrderMatters()
        {
            // Arrange - swapping artist and album should produce different hash
            const string value1 = "First";
            const string value2 = "Second";

            // Act
            var hash1 = HashingUtilities.CoverIdentifier(value1, value2);
            var hash2 = HashingUtilities.CoverIdentifier(value2, value1);

            // Assert
            hash1.Should().NotBe(hash2);
        }

        #endregion

        #region GetCoverStoragePath

        [Fact]
        public void GetCoverStoragePath_ValidPath_ReturnsCombinedPath()
        {
            // Arrange
            const string basePath = @"C:\MusicBee";
            var expectedPath = Path.Combine(basePath, @"cache\covers");

            // Act
            var result = HashingUtilities.GetCoverStoragePath(basePath);

            // Assert
            result.Should().Be(expectedPath);
        }

        [Fact]
        public void GetCoverStoragePath_EmptyPath_ReturnsRelativePath()
        {
            // Arrange
            var expectedPath = @"cache\covers";

            // Act
            var result = HashingUtilities.GetCoverStoragePath(string.Empty);

            // Assert
            result.Should().Be(expectedPath);
        }

        [Fact]
        public void GetCoverStoragePath_PathWithTrailingSlash_Works()
        {
            // Arrange
            const string basePath = @"C:\MusicBee\";

            // Act
            var result = HashingUtilities.GetCoverStoragePath(basePath);

            // Assert
            result.Should().Contain("cache");
            result.Should().Contain("covers");
        }

        #endregion

        #region EmptyHash Property

        [Fact]
        public void EmptyHash_HasCorrectLength()
        {
            // Assert
            HashingUtilities.EmptyHash.Should().HaveLength(Sha1HashLength);
        }

        [Fact]
        public void EmptyHash_IsAllZeros()
        {
            // Assert
            HashingUtilities.EmptyHash.Should().Be(new string('0', Sha1HashLength));
        }

        [Fact]
        public void EmptyHash_IsConsistent()
        {
            // Act
            var hash1 = HashingUtilities.EmptyHash;
            var hash2 = HashingUtilities.EmptyHash;

            // Assert
            hash1.Should().Be(hash2);
        }

        #endregion
    }
}
