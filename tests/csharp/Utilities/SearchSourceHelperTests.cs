using FluentAssertions;
using Moq;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Data;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class SearchSourceHelperTests
    {
        private readonly Mock<IUserSettings> _mockUserSettings;

        public SearchSourceHelperTests()
        {
            _mockUserSettings = new Mock<IUserSettings>();
        }

        [Fact]
        public void GetSearchSource_ReturnsLibrary_WhenSourceIsNone()
        {
            // Arrange
            _mockUserSettings.Setup(s => s.Source).Returns(SearchSource.None);

            // Act
            var result = SearchSourceHelper.GetSearchSource(_mockUserSettings.Object);

            // Assert
            result.Should().Be(SearchSource.Library);
        }

        [Fact]
        public void GetSearchSource_ReturnsLibrary_WhenSourceIsLibrary()
        {
            // Arrange
            _mockUserSettings.Setup(s => s.Source).Returns(SearchSource.Library);

            // Act
            var result = SearchSourceHelper.GetSearchSource(_mockUserSettings.Object);

            // Assert
            result.Should().Be(SearchSource.Library);
        }

        [Fact]
        public void GetSearchSource_ReturnsInbox_WhenSourceIsInbox()
        {
            // Arrange
            _mockUserSettings.Setup(s => s.Source).Returns(SearchSource.Inbox);

            // Act
            var result = SearchSourceHelper.GetSearchSource(_mockUserSettings.Object);

            // Assert
            result.Should().Be(SearchSource.Inbox);
        }

        [Fact]
        public void GetSearchSource_ReturnsPodcasts_WhenSourceIsPodcasts()
        {
            // Arrange
            _mockUserSettings.Setup(s => s.Source).Returns(SearchSource.Podcasts);

            // Act
            var result = SearchSourceHelper.GetSearchSource(_mockUserSettings.Object);

            // Assert
            result.Should().Be(SearchSource.Podcasts);
        }

        [Fact]
        public void GetSearchSource_ReturnsAudiobooks_WhenSourceIsAudiobooks()
        {
            // Arrange
            _mockUserSettings.Setup(s => s.Source).Returns(SearchSource.Audiobooks);

            // Act
            var result = SearchSourceHelper.GetSearchSource(_mockUserSettings.Object);

            // Assert
            result.Should().Be(SearchSource.Audiobooks);
        }

        [Fact]
        public void GetSearchSource_ReturnsVideos_WhenSourceIsVideos()
        {
            // Arrange
            _mockUserSettings.Setup(s => s.Source).Returns(SearchSource.Videos);

            // Act
            var result = SearchSourceHelper.GetSearchSource(_mockUserSettings.Object);

            // Assert
            result.Should().Be(SearchSource.Videos);
        }

        [Theory]
        [InlineData(SearchSource.Library)]
        [InlineData(SearchSource.Inbox)]
        [InlineData(SearchSource.Podcasts)]
        [InlineData(SearchSource.Audiobooks)]
        [InlineData(SearchSource.Videos)]
        public void GetSearchSource_ReturnsConfiguredSource_WhenNotNone(SearchSource source)
        {
            // Arrange
            _mockUserSettings.Setup(s => s.Source).Returns(source);

            // Act
            var result = SearchSourceHelper.GetSearchSource(_mockUserSettings.Object);

            // Assert
            result.Should().Be(source);
        }

        [Fact]
        public void GetSearchSource_DefaultsToLibrary_ForCombinedFlagsWithNone()
        {
            // Arrange - SearchSource.None is 0, so combining with anything gives that value
            _mockUserSettings.Setup(s => s.Source).Returns(SearchSource.None);

            // Act
            var result = SearchSourceHelper.GetSearchSource(_mockUserSettings.Object);

            // Assert
            result.Should().Be(SearchSource.Library);
        }
    }
}
