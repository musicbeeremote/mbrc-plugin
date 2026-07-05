using FluentAssertions;
using MusicBeePlugin.Services.Media;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Services
{
    public class CoverCacheTests
    {
        private readonly MockLogger _logger;
        private readonly CoverCache _sut;

        public CoverCacheTests()
        {
            _logger = new MockLogger();
            // Use a temp path that we won't actually write to for most tests
            _sut = new CoverCache(_logger, System.IO.Path.GetTempPath());
        }

        [Fact]
        public void Constructor_InitializesEmptyCache()
        {
            // Assert
            _sut.Count.Should().Be(0);
            _sut.State.Should().Be("0");
        }

        [Fact]
        public void Update_AddsCoverToCache()
        {
            // Act
            _sut.Update("album-key", "cover-hash-123");

            // Assert
            _sut.Count.Should().Be(1);
        }

        [Fact]
        public void Update_OverwritesExistingCover()
        {
            // Arrange
            _sut.Update("album-key", "old-hash");

            // Act
            _sut.Update("album-key", "new-hash");

            // Assert
            _sut.Count.Should().Be(1);
            _sut.GetCoverHash("album-key").Should().Be("new-hash");
        }

        [Fact]
        public void IsCached_ReturnsFalse_WhenKeyNotPresent()
        {
            // Act
            var result = _sut.IsCached("nonexistent-key");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsCached_ReturnsTrue_WhenKeyPresent()
        {
            // Arrange
            _sut.Update("album-key", "cover-hash");

            // Act
            var result = _sut.IsCached("album-key");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void GetCoverHash_ReturnsNull_WhenKeyNotPresent()
        {
            // Act
            var result = _sut.GetCoverHash("nonexistent-key");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetCoverHash_ReturnsHash_WhenKeyPresent()
        {
            // Arrange
            _sut.Update("album-key", "cover-hash-456");

            // Act
            var result = _sut.GetCoverHash("album-key");

            // Assert
            result.Should().Be("cover-hash-456");
        }

        [Fact]
        public void Keys_ReturnsEmptyEnumerable_WhenCacheEmpty()
        {
            // Act
            var result = _sut.Keys();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void Keys_ReturnsAllKeys_WhenCacheHasItems()
        {
            // Arrange
            _sut.Update("key1", "hash1");
            _sut.Update("key2", "hash2");
            _sut.Update("key3", "hash3");

            // Act
            var result = _sut.Keys();

            // Assert
            result.Should().HaveCount(3);
            result.Should().Contain(new[] { "key1", "key2", "key3" });
        }

        [Fact]
        public void State_ReturnsCount_AsString()
        {
            // Arrange
            _sut.Update("key1", "hash1");
            _sut.Update("key2", "hash2");

            // Act
            var result = _sut.State;

            // Assert
            result.Should().Be("2");
        }

        [Fact]
        public void Count_ReturnsCorrectCount()
        {
            // Arrange
            _sut.Update("key1", "hash1");
            _sut.Update("key2", "hash2");
            _sut.Update("key3", "hash3");
            _sut.Update("key4", "hash4");

            // Act
            var result = _sut.Count;

            // Assert
            result.Should().Be(4);
        }

        [Fact]
        public void Lookup_ReturnsEmptyString_WhenKeyNotPresent()
        {
            // Act
            var result = _sut.Lookup("nonexistent-key");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetCoverInfo_ReturnsNullHashAndEmptyPath_WhenKeyNotPresent()
        {
            // Act
            var (hash, path) = _sut.GetCoverInfo("nonexistent-key");

            // Assert
            hash.Should().BeNull();
            path.Should().BeEmpty();
        }

        [Fact]
        public void GetCoverInfo_ReturnsHash_WhenOnlyCoverCached()
        {
            // Arrange
            _sut.Update("album-key", "cover-hash");

            // Act
            var (hash, path) = _sut.GetCoverInfo("album-key");

            // Assert
            hash.Should().Be("cover-hash");
            path.Should().BeEmpty(); // Path not set via Update
        }

        [Fact]
        public void Update_WithMultipleKeys_StoresAllCorrectly()
        {
            // Arrange & Act
            _sut.Update("album1", "hash1");
            _sut.Update("album2", "hash2");
            _sut.Update("album3", "hash3");

            // Assert
            _sut.GetCoverHash("album1").Should().Be("hash1");
            _sut.GetCoverHash("album2").Should().Be("hash2");
            _sut.GetCoverHash("album3").Should().Be("hash3");
        }

        [Fact]
        public void Update_WithEmptyKey_StillWorks()
        {
            // Act
            _sut.Update("", "hash-for-empty-key");

            // Assert
            _sut.IsCached("").Should().BeTrue();
            _sut.GetCoverHash("").Should().Be("hash-for-empty-key");
        }

        [Fact]
        public void Update_WithEmptyHash_StillWorks()
        {
            // Act
            _sut.Update("album-key", "");

            // Assert
            _sut.IsCached("album-key").Should().BeTrue();
            _sut.GetCoverHash("album-key").Should().BeEmpty();
        }

        [Fact]
        public void Update_WithNullHash_StillWorks()
        {
            // Act
            _sut.Update("album-key", null);

            // Assert
            _sut.IsCached("album-key").Should().BeTrue();
            _sut.GetCoverHash("album-key").Should().BeNull();
        }

        [Theory]
        [InlineData("simple-key")]
        [InlineData("key/with/slashes")]
        [InlineData("key with spaces")]
        [InlineData("key-with-special-chars!@#$%")]
        [InlineData("unicode-키-ключ-مفتاح")]
        public void Update_AcceptsVariousKeyFormats(string key)
        {
            // Act
            _sut.Update(key, "test-hash");

            // Assert
            _sut.IsCached(key).Should().BeTrue();
            _sut.GetCoverHash(key).Should().Be("test-hash");
        }
    }
}
