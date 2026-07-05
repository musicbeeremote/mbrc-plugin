using System.Linq;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.DataProviders
{
    /// <summary>
    ///     Tests for ITrackDataProvider interface behavior using mock implementation.
    ///     Tests verify the contract behavior that implementations must follow.
    /// </summary>
    public class TrackDataProviderTests
    {
        private readonly MockTrackDataProvider _provider;

        public TrackDataProviderTests()
        {
            _provider = new MockTrackDataProvider();
        }

        #region Now Playing Track Info Tests

        [Fact]
        public void GetNowPlayingFileUrl_ReturnsConfiguredUrl()
        {
            _provider.CurrentFileUrl = "/test/path.mp3";
            Assert.Equal("/test/path.mp3", _provider.GetNowPlayingFileUrl());
        }

        [Fact]
        public void GetNowPlayingTrackInfo_ReturnsTrackInfo()
        {
            var info = _provider.GetNowPlayingTrackInfo();

            Assert.NotNull(info);
            Assert.Equal("Test Artist", info.Artist);
            Assert.Equal("Test Title", info.Title);
            Assert.Equal("Test Album", info.Album);
        }

        [Fact]
        public void GetNowPlayingTrackDetails_ReturnsDetails()
        {
            var details = _provider.GetNowPlayingTrackDetails();

            Assert.NotNull(details);
            Assert.Equal("Test Album Artist", details.AlbumArtist);
            Assert.Equal("Rock", details.Genre);
        }

        [Fact]
        public void GetPlaybackPosition_ReturnsPosition()
        {
            _provider.CurrentPlaybackPosition = new PlaybackPosition(30000, 180000);

            var position = _provider.GetPlaybackPosition();

            Assert.Equal(30000, position.Current);
            Assert.Equal(180000, position.Total);
        }

        #endregion

        #region Media Content Tests

        [Fact]
        public void GetNowPlayingArtwork_ReturnsPath()
        {
            _provider.Artwork = "/art/cover.jpg";
            Assert.Equal("/art/cover.jpg", _provider.GetNowPlayingArtwork());
        }

        [Fact]
        public void GetNowPlayingLyrics_ReturnsLyrics()
        {
            _provider.Lyrics = "Hello world";
            Assert.Equal("Hello world", _provider.GetNowPlayingLyrics());
        }

        #endregion

        #region Now Playing List Tests

        [Fact]
        public void SearchNowPlayingList_WhenFound_ReturnsPath()
        {
            _provider.NowPlayingList.Add(new NowPlaying
            {
                Artist = "Test Artist",
                Title = "Matching Song",
                Path = "/music/song.mp3"
            });

            var result = _provider.SearchNowPlayingList("Matching", SearchSource.Library);

            Assert.Equal("/music/song.mp3", result);
        }

        [Fact]
        public void SearchNowPlayingList_WhenNotFound_ReturnsNull()
        {
            _provider.NowPlayingList.Add(new NowPlaying
            {
                Artist = "Test Artist",
                Title = "Some Song",
                Path = "/music/song.mp3"
            });

            var result = _provider.SearchNowPlayingList("NonExistent", SearchSource.Library);

            Assert.Null(result);
        }

        [Fact]
        public void GetNowPlayingListLegacy_ReturnsList()
        {
            _provider.LegacyNowPlayingList.Add(new NowPlayingListTrack
            {
                Artist = "Artist 1",
                Title = "Track 1",
                Position = 1
            });

            var list = _provider.GetNowPlayingListLegacy().ToList();

            Assert.Single(list);
            Assert.Equal("Track 1", list[0].Title);
        }

        #endregion

        #region Rating Tests

        [Fact]
        public void GetNowPlayingRating_ReturnsRating()
        {
            _provider.CurrentRating = "5";
            Assert.Equal("5", _provider.GetNowPlayingRating());
        }

        [Fact]
        public void SetNowPlayingRating_SetsRating()
        {
            _provider.SetNowPlayingRating("3.5");

            Assert.Equal("3.5", _provider.CurrentRating);
            Assert.Equal(1, _provider.SetRatingCallCount);
        }

        #endregion

        #region Last.fm Tests

        [Fact]
        public void GetNowPlayingLastfmStatus_ReturnsStatus()
        {
            _provider.CurrentLastfmStatus = LastfmStatus.Love;
            Assert.Equal(LastfmStatus.Love, _provider.GetNowPlayingLastfmStatus());
        }

        [Fact]
        public void SetNowPlayingLastfmStatus_SetsStatus()
        {
            _provider.SetNowPlayingLastfmStatus(LastfmStatus.Ban);

            Assert.Equal(LastfmStatus.Ban, _provider.CurrentLastfmStatus);
            Assert.Equal(1, _provider.SetLastfmStatusCallCount);
        }

        [Theory]
        [InlineData(LastfmStatus.Normal)]
        [InlineData(LastfmStatus.Love)]
        [InlineData(LastfmStatus.Ban)]
        public void SetNowPlayingLastfmStatus_AllValues_Succeed(LastfmStatus status)
        {
            Assert.True(_provider.SetNowPlayingLastfmStatus(status));
            Assert.Equal(status, _provider.GetNowPlayingLastfmStatus());
        }

        #endregion

        #region Tag Operations Tests

        [Fact]
        public void SetTrackTag_IncreasesCallCount()
        {
            _provider.SetTrackTag("/path/file.mp3", "Title", "New Title");

            Assert.Equal(1, _provider.SetTrackTagCallCount);
        }

        [Fact]
        public void CommitTrackTags_IncreasesCallCount()
        {
            _provider.CommitTrackTags("/path/file.mp3");

            Assert.Equal(1, _provider.CommitTrackTagsCallCount);
        }

        #endregion
    }
}
