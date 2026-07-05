using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Configuration;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Services.Media;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Services
{
    public class NotificationHandlerTests
    {
        private readonly Mock<ICoverService> _coverService;
        private readonly Mock<IPlayerDataProvider> _playerDataProvider;
        private readonly Mock<ITrackDataProvider> _trackDataProvider;
        private readonly Mock<LyricCoverModel> _lyricCoverModel;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly MockLogger _logger;
        private readonly Mock<IBroadcaster> _broadcaster;
        private readonly NotificationHandler _sut;

        public NotificationHandlerTests()
        {
            _coverService = new Mock<ICoverService>();
            _playerDataProvider = new Mock<IPlayerDataProvider>();
            _trackDataProvider = new Mock<ITrackDataProvider>();
            _eventAggregator = new Mock<IEventAggregator>();
            _logger = new MockLogger();
            _broadcaster = new Mock<IBroadcaster>();

            // Create LyricCoverModel mock
            _lyricCoverModel = new Mock<LyricCoverModel>(_logger, _broadcaster.Object);

            _sut = new NotificationHandler(
                _coverService.Object,
                _playerDataProvider.Object,
                _trackDataProvider.Object,
                _lyricCoverModel.Object,
                _eventAggregator.Object,
                _logger);
        }

        #region Track Changed Notifications

        [Fact]
        public void HandleTrackChanged_BroadcastsTrackInfo()
        {
            // Arrange
            var trackV2 = new NowPlayingTrackV2 { Artist = "Artist", Album = "Album", Title = "Title", Year = "2023" };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(trackV2);
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns("artwork");
            _trackDataProvider.Setup(x => x.GetNowPlayingDownloadedArtwork()).Returns(string.Empty);
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("lyrics");
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(new PlaybackPosition());

            // Act
            _sut.HandleTrackChanged("test-track.mp3");

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackInfo(), Times.AtLeastOnce);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<BroadcastEvent>()), Times.AtLeastOnce);
        }

        [Fact]
        public void HandleTrackChanged_UpdatesLyricCoverModel()
        {
            // Arrange
            var trackV2 = new NowPlayingTrackV2 { Artist = "Artist", Album = "Album", Title = "Title" };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(trackV2);
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns("artwork-data");
            _trackDataProvider.Setup(x => x.GetNowPlayingDownloadedArtwork()).Returns(string.Empty);
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("5");
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Love);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("test lyrics");
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(new PlaybackPosition());

            // Act
            _sut.HandleTrackChanged("track.mp3");

            // Assert
            _lyricCoverModel.Verify(x => x.SetCover("artwork-data"), Times.Once);
        }

        [Fact]
        public void HandleTrackChanged_NoArtwork_UsesDownloadedArtwork()
        {
            // Arrange
            var trackV2 = new NowPlayingTrackV2 { Artist = "Artist", Album = "Album", Title = "Title" };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(trackV2);
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns(string.Empty);
            _trackDataProvider.Setup(x => x.GetNowPlayingDownloadedArtwork()).Returns("downloaded-artwork");
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("lyrics");
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(new PlaybackPosition());

            // Act
            _sut.HandleTrackChanged("track.mp3");

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingDownloadedArtwork(), Times.Once);
            _lyricCoverModel.Verify(x => x.SetCover("downloaded-artwork"), Times.Once);
        }

        #endregion

        #region Volume Notifications

        [Fact]
        public void HandleVolumeLevelChanged_BroadcastsVolume()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetVolume()).Returns(75);

            // Act
            _sut.HandleVolumeLevelChanged();

            // Assert
            _playerDataProvider.Verify(x => x.GetVolume(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<MessageSendEvent>()), Times.Once);
        }

        [Fact]
        public void HandleVolumeMuteChanged_BroadcastsMuteState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetMute()).Returns(true);

            // Act
            _sut.HandleVolumeMuteChanged();

            // Assert
            _playerDataProvider.Verify(x => x.GetMute(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<MessageSendEvent>()), Times.Once);
        }

        #endregion

        #region Play State Notifications

        [Fact]
        public void HandlePlayStateChanged_BroadcastsPlayState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetPlayState()).Returns(PlayState.Playing);

            // Act
            _sut.HandlePlayStateChanged();

            // Assert
            _playerDataProvider.Verify(x => x.GetPlayState(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<MessageSendEvent>()), Times.Once);
        }

        #endregion

        #region Lyrics and Artwork Ready Notifications

        [Fact]
        public void HandleNowPlayingLyricsReady_UpdatesLyricCoverModel()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("New lyrics content");

            // Act
            _sut.HandleNowPlayingLyricsReady();

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingLyrics(), Times.Once);
        }

        [Fact]
        public void HandleNowPlayingArtworkReady_UpdatesLyricCoverModel()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingDownloadedArtwork()).Returns("downloaded-cover-data");

            // Act
            _sut.HandleNowPlayingArtworkReady();

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingDownloadedArtwork(), Times.Once);
            _lyricCoverModel.Verify(x => x.SetCover("downloaded-cover-data"), Times.Once);
        }

        #endregion

        #region Now Playing List Changed

        [Fact]
        public void HandleNowPlayingListChanged_BroadcastsListChangedEvent()
        {
            // Act
            _sut.HandleNowPlayingListChanged();

            // Assert
            _eventAggregator.Verify(x => x.Publish(It.IsAny<MessageSendEvent>()), Times.Once);
        }

        #endregion

        #region File Added to Library

        [Fact]
        public void HandleFileAddedToLibrary_CachesTrackCover()
        {
            // Arrange
            var fileUrl = "new-track.mp3";

            // Act
            _sut.HandleFileAddedToLibrary(fileUrl);

            // Assert
            _coverService.Verify(x => x.CacheTrackCover(fileUrl), Times.Once);
        }

        #endregion

        #region Broadcast Initial State

        [Fact]
        public void BroadcastInitialNowPlayingState_WithTrack_BroadcastsCoverAndLyrics()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns("current-track.mp3");
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns("cover-data");
            _trackDataProvider.Setup(x => x.GetNowPlayingDownloadedArtwork()).Returns(string.Empty);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("lyrics content");

            // Act
            _sut.BroadcastInitialNowPlayingState();

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingFileUrl(), Times.Once);
            _lyricCoverModel.Verify(x => x.SetCover("cover-data"), Times.Once);
        }

        [Fact]
        public void BroadcastInitialNowPlayingState_NoTrack_SkipsBroadcast()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns(string.Empty);

            // Act
            _sut.BroadcastInitialNowPlayingState();

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingFileUrl(), Times.Once);
            _trackDataProvider.Verify(x => x.GetNowPlayingArtwork(), Times.Never);
            _trackDataProvider.Verify(x => x.GetNowPlayingLyrics(), Times.Never);
        }

        #endregion

        #region Error Handling

        [Fact]
        public void HandleVolumeLevelChanged_ExceptionThrown_LogsError()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetVolume()).Throws(new System.InvalidOperationException("Test exception"));

            // Act
            _sut.HandleVolumeLevelChanged();

            // Assert
            _logger.ErrorMessages.Should().NotBeEmpty();
        }

        [Fact]
        public void HandlePlayStateChanged_ExceptionThrown_LogsError()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetPlayState()).Throws(new System.InvalidOperationException("Test exception"));

            // Act
            _sut.HandlePlayStateChanged();

            // Assert
            _logger.ErrorMessages.Should().NotBeEmpty();
        }

        #endregion
    }
}
