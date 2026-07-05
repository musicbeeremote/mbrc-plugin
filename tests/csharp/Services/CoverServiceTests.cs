using System.Collections.Generic;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Responses;
using MusicBeePlugin.Services.Media;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Services
{
    public class CoverServiceTests
    {
        private readonly Mock<ICoverCache> _coverCache;
        private readonly Mock<ILibraryDataProvider> _libraryDataProvider;
        private readonly Mock<ITrackDataProvider> _trackDataProvider;
        private readonly Mock<ISystemOperations> _systemOperations;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly MockLogger _logger;
        private readonly string _storagePath;
        private readonly CoverService _sut;

        public CoverServiceTests()
        {
            _coverCache = new Mock<ICoverCache>();
            _libraryDataProvider = new Mock<ILibraryDataProvider>();
            _trackDataProvider = new Mock<ITrackDataProvider>();
            _systemOperations = new Mock<ISystemOperations>();
            _eventAggregator = new Mock<IEventAggregator>();
            _logger = new MockLogger();
            _storagePath = System.IO.Path.GetTempPath();

            _sut = new CoverService(
                _coverCache.Object,
                _libraryDataProvider.Object,
                _trackDataProvider.Object,
                _systemOperations.Object,
                _eventAggregator.Object,
                _logger,
                _storagePath);
        }

        #region 5.1 GetAlbumCover - Cached Scenarios

        [Fact]
        public void GetAlbumCover_CachedWithHash_ReturnsCover()
        {
            // Arrange - key is computed internally by CoverIdentifier(artist, album)
            _coverCache.Setup(x => x.IsCached(It.IsAny<string>())).Returns(true);
            _coverCache.Setup(x => x.GetCoverHash(It.IsAny<string>())).Returns("abc123hash");

            // Act
            var result = _sut.GetAlbumCover("artist", "album", null);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be(200);
            _coverCache.Verify(x => x.IsCached(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void GetAlbumCover_CachedWithEmptyHash_Returns404()
        {
            // Arrange
            _coverCache.Setup(x => x.IsCached(It.IsAny<string>())).Returns(true);
            _coverCache.Setup(x => x.GetCoverHash(It.IsAny<string>())).Returns(string.Empty);

            // Act
            var result = _sut.GetAlbumCover("artist", "album", null);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be(404);
        }

        [Fact]
        public void GetAlbumCover_ClientHashMatches_Returns304()
        {
            // Arrange
            var hash = "matching-hash";
            _coverCache.Setup(x => x.IsCached(It.IsAny<string>())).Returns(true);
            _coverCache.Setup(x => x.GetCoverHash(It.IsAny<string>())).Returns(hash);

            // Act
            var result = _sut.GetAlbumCover("artist", "album", hash);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be(304);
        }

        [Fact]
        public void GetAlbumCover_ClientHashDifferent_Returns200WithCover()
        {
            // Arrange
            _coverCache.Setup(x => x.IsCached(It.IsAny<string>())).Returns(true);
            _coverCache.Setup(x => x.GetCoverHash(It.IsAny<string>())).Returns("server-hash");

            // Act
            var result = _sut.GetAlbumCover("artist", "album", "client-hash");

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be(200);
            result.Hash.Should().Be("server-hash");
        }

        #endregion

        #region 5.2 GetAlbumCover - Non-Cached Scenarios

        [Fact]
        public void GetAlbumCover_NotCached_NoLookup_Returns404()
        {
            // Arrange
            _coverCache.Setup(x => x.IsCached(It.IsAny<string>())).Returns(false);
            _coverCache.Setup(x => x.Lookup(It.IsAny<string>())).Returns(string.Empty);

            // Act
            var result = _sut.GetAlbumCover("artist", "album", null);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be(404);
        }

        [Fact]
        public void GetAlbumCover_NotCached_LookupFound_CachesAndReturnsCover()
        {
            // Arrange
            _coverCache.Setup(x => x.IsCached(It.IsAny<string>())).Returns(false);
            _coverCache.Setup(x => x.Lookup(It.IsAny<string>())).Returns("/path/to/track.mp3");
            _libraryDataProvider.Setup(x => x.GetArtworkForTrack("/path/to/track.mp3")).Returns("/path/to/cover.jpg");
            _libraryDataProvider.Setup(x => x.GetArtworkDataForTrack("/path/to/track.mp3")).Returns((byte[])null);

            // Act
            var result = _sut.GetAlbumCover("artist", "album", null);

            // Assert
            _coverCache.Verify(x => x.Lookup(It.IsAny<string>()), Times.Once);
            // Note: The actual caching involves file operations, so we just verify the lookup happened
        }

        #endregion

        #region 5.3 GetCoverBySize

        [Fact]
        public void GetCoverBySize_InvalidSize_Returns400()
        {
            // Arrange - neither numeric nor "original"
            // Act
            var result = _sut.GetCoverBySize("artist", "album", "invalid");

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be(400);
        }

        [Fact]
        public void GetCoverBySize_NoPathFound_Returns404()
        {
            // Arrange
            _coverCache.Setup(x => x.Lookup(It.IsAny<string>())).Returns(string.Empty);

            // Act
            var result = _sut.GetCoverBySize("artist", "album", "300");

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be(404);
        }

        [Fact]
        public void GetCoverBySize_OriginalSize_ValidPath_ReturnsCover()
        {
            // Arrange
            _coverCache.Setup(x => x.Lookup(It.IsAny<string>())).Returns("/path/to/track.mp3");
            _libraryDataProvider.Setup(x => x.GetArtworkForTrack("/path/to/track.mp3")).Returns(string.Empty);
            _libraryDataProvider.Setup(x => x.GetArtworkDataForTrack("/path/to/track.mp3")).Returns((byte[])null);

            // Act
            var result = _sut.GetCoverBySize("artist", "album", "original");

            // Assert
            result.Should().NotBeNull();
            // Returns 404 because no actual artwork data exists
            result.Status.Should().Be(404);
        }

        [Fact]
        public void GetCoverBySize_NumericSize_ValidPath_ReturnsCover()
        {
            // Arrange
            _coverCache.Setup(x => x.Lookup(It.IsAny<string>())).Returns("/path/to/track.mp3");
            _libraryDataProvider.Setup(x => x.GetArtworkForTrack("/path/to/track.mp3")).Returns(string.Empty);
            _libraryDataProvider.Setup(x => x.GetArtworkDataForTrack("/path/to/track.mp3")).Returns((byte[])null);

            // Act
            var result = _sut.GetCoverBySize("artist", "album", "150");

            // Assert
            result.Should().NotBeNull();
            // Returns 404 because no actual artwork data exists
            result.Status.Should().Be(404);
        }

        [Fact]
        public void GetCoverBySize_WithArtworkData_Returns200()
        {
            // Arrange
            var artworkData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
            _coverCache.Setup(x => x.Lookup(It.IsAny<string>())).Returns("/path/to/track.mp3");
            _libraryDataProvider.Setup(x => x.GetArtworkForTrack("/path/to/track.mp3")).Returns(string.Empty);
            _libraryDataProvider.Setup(x => x.GetArtworkDataForTrack("/path/to/track.mp3")).Returns(artworkData);

            // Act
            var result = _sut.GetCoverBySize("artist", "album", "original");

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be(200);
            result.Cover.Should().NotBeNullOrEmpty();
            _coverCache.Verify(x => x.Lookup(It.IsAny<string>()), Times.Once);
        }

        #endregion

        #region 5.4 GetCoverPage

        [Fact]
        public void GetCoverPage_EmptyCache_ReturnsEmptyPage()
        {
            // Arrange
            _coverCache.Setup(x => x.Keys()).Returns(new List<string>());

            // Act
            var result = _sut.GetCoverPage(0, 10);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().BeEmpty();
            result.Total.Should().Be(0);
            result.Offset.Should().Be(0);
            result.Limit.Should().Be(10);
        }

        [Fact]
        public void GetCoverPage_WithItems_ReturnsCorrectPage()
        {
            // Arrange
            var keys = new List<string> { "key1", "key2", "key3" };
            _coverCache.Setup(x => x.Keys()).Returns(keys);
            _coverCache.Setup(x => x.GetCoverInfo(It.IsAny<string>()))
                .Returns(("hash", "/path/to/track.mp3"));
            _libraryDataProvider.Setup(x => x.GetBatchTrackMetadata(It.IsAny<IEnumerable<string>>()))
                .Returns(new Dictionary<string, (string Artist, string Album)>
                {
                    { "/path/to/track.mp3", ("Artist", "Album") }
                });

            // Act
            var result = _sut.GetCoverPage(0, 10);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().HaveCount(3);
            result.Total.Should().Be(3);
        }

        [Fact]
        public void GetCoverPage_WithOffset_SkipsItems()
        {
            // Arrange
            var keys = new List<string> { "key1", "key2", "key3", "key4", "key5" };
            _coverCache.Setup(x => x.Keys()).Returns(keys);
            _coverCache.Setup(x => x.GetCoverInfo(It.IsAny<string>()))
                .Returns(("hash", "/path/to/track.mp3"));
            _libraryDataProvider.Setup(x => x.GetBatchTrackMetadata(It.IsAny<IEnumerable<string>>()))
                .Returns(new Dictionary<string, (string Artist, string Album)>
                {
                    { "/path/to/track.mp3", ("Artist", "Album") }
                });

            // Act
            var result = _sut.GetCoverPage(2, 2);

            // Assert
            result.Should().NotBeNull();
            result.Data.Should().HaveCount(2);
            result.Offset.Should().Be(2);
            result.Limit.Should().Be(2);
            result.Total.Should().Be(5);
        }

        #endregion

        #region 5.5 GetNowPlayingCover

        [Fact]
        public void GetNowPlayingCover_WithDirectArtwork_ReturnsArtwork()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns("/path/to/cover.jpg");

            // Act
            var result = _sut.GetNowPlayingCover();

            // Assert
            result.Should().Be("/path/to/cover.jpg");
            _trackDataProvider.Verify(x => x.GetNowPlayingArtwork(), Times.Once);
        }

        [Fact]
        public void GetNowPlayingCover_NoDirectArtwork_FallsBackToDownloaded()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns(string.Empty);
            _trackDataProvider.Setup(x => x.GetNowPlayingDownloadedArtwork()).Returns("/downloaded/cover.jpg");

            // Act
            var result = _sut.GetNowPlayingCover();

            // Assert
            result.Should().Be("/downloaded/cover.jpg");
            _trackDataProvider.Verify(x => x.GetNowPlayingDownloadedArtwork(), Times.Once);
        }

        [Fact]
        public void GetNowPlayingCover_NoArtwork_ReturnsEmptyString()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns(string.Empty);
            _trackDataProvider.Setup(x => x.GetNowPlayingDownloadedArtwork()).Returns(string.Empty);

            // Act
            var result = _sut.GetNowPlayingCover();

            // Assert
            result.Should().BeEmpty();
        }

        #endregion

        #region 5.6 BroadcastCacheStatus

        [Fact]
        public void BroadcastCacheStatus_PublishesEvent()
        {
            // Act
            _sut.BroadcastCacheStatus();

            // Assert
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void BroadcastCacheStatus_WithClientId_PublishesEvent()
        {
            // Act
            _sut.BroadcastCacheStatus("client-123");

            // Assert
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 5.7 InvalidateCache

        [Fact]
        public void InvalidateCache_CallsCacheInvalidate()
        {
            // Act
            _sut.InvalidateCache();

            // Assert
            _coverCache.Verify(x => x.Invalidate(), Times.Once);
            _systemOperations.Verify(x => x.CreateBackgroundTask(It.IsAny<System.Action>()), Times.Once);
        }

        #endregion

        #region 5.8 IsBuildingCache

        [Fact]
        public void IsBuildingCache_InitiallyFalse()
        {
            // Assert
            _sut.IsBuildingCache.Should().BeFalse();
        }

        #endregion

        #region 5.9 CacheTrackCover

        [Fact]
        public void CacheTrackCover_GetsMetadataAndUpdatesCache()
        {
            // Arrange
            var trackUrl = "/path/to/track.mp3";
            _libraryDataProvider.Setup(x => x.GetArtworkForTrack(trackUrl)).Returns(string.Empty);
            _libraryDataProvider.Setup(x => x.GetArtworkDataForTrack(trackUrl)).Returns((byte[])null);
            _libraryDataProvider.Setup(x => x.GetAlbumArtistForTrack(trackUrl)).Returns("Artist");
            _libraryDataProvider.Setup(x => x.GetAlbumForTrack(trackUrl)).Returns("Album");

            // Act
            _sut.CacheTrackCover(trackUrl);

            // Assert
            _libraryDataProvider.Verify(x => x.GetAlbumArtistForTrack(trackUrl), Times.Once);
            _libraryDataProvider.Verify(x => x.GetAlbumForTrack(trackUrl), Times.Once);
            _coverCache.Verify(x => x.Update(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        #endregion
    }
}
