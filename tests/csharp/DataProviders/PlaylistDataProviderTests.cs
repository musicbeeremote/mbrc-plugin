using System.Linq;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.DataProviders
{
    /// <summary>
    ///     Tests for IPlaylistDataProvider interface behavior using mock implementation.
    ///     Tests verify the contract behavior that implementations must follow.
    /// </summary>
    public class PlaylistDataProviderTests
    {
        private readonly MockPlaylistDataProvider _provider;

        public PlaylistDataProviderTests()
        {
            _provider = new MockPlaylistDataProvider();
        }

        #region Now Playing List Management Tests

        [Fact]
        public void MoveNowPlayingTrack_RecordsParameters()
        {
            _provider.MoveNowPlayingTrack(0, 5);

            Assert.Equal(1, _provider.MoveTrackCallCount);
            Assert.Equal(0, _provider.LastMoveFromIndex);
            Assert.Equal(5, _provider.LastMoveToIndex);
        }

        [Fact]
        public void MoveNowPlayingTrack_ReturnsTrue()
        {
            var result = _provider.MoveNowPlayingTrack(1, 3);
            Assert.True(result);
        }

        [Fact]
        public void RemoveFromNowPlayingList_ValidIndex_RemovesItem()
        {
            _provider.NowPlayingFiles.Add("/music/track1.mp3");
            _provider.NowPlayingFiles.Add("/music/track2.mp3");

            var result = _provider.RemoveFromNowPlayingList(0);

            Assert.True(result);
            Assert.Single(_provider.NowPlayingFiles);
            Assert.Equal("/music/track2.mp3", _provider.NowPlayingFiles[0]);
        }

        [Fact]
        public void RemoveFromNowPlayingList_InvalidIndex_ReturnsFalse()
        {
            var result = _provider.RemoveFromNowPlayingList(999);

            Assert.False(result);
            Assert.Equal(1, _provider.RemoveTrackCallCount);
        }

        [Fact]
        public void PlayNowPlayingTrack_IncreasesCallCount()
        {
            _provider.PlayNowPlayingTrack("/music/song.mp3");

            Assert.Equal(1, _provider.PlayTrackCallCount);
        }

        [Fact]
        public void PlayNowPlayingByIndex_ValidIndex_ReturnsTrue()
        {
            _provider.NowPlayingFiles.Add("/music/track.mp3");

            var result = _provider.PlayNowPlayingByIndex(0);

            Assert.True(result);
            Assert.Equal(1, _provider.PlayTrackCallCount);
        }

        [Fact]
        public void PlayNowPlayingByIndex_InvalidIndex_ReturnsFalse()
        {
            var result = _provider.PlayNowPlayingByIndex(999);

            Assert.False(result);
        }

        #endregion

        #region Queue Operations Tests

        [Fact]
        public void QueueFiles_Next_RecordsParameters()
        {
            var files = new[] { "/music/song1.mp3", "/music/song2.mp3" };

            _provider.QueueFiles(QueueType.Next, files);

            Assert.Equal(1, _provider.QueueFilesCallCount);
            Assert.Equal(QueueType.Next, _provider.LastQueueType);
            Assert.Equal(files, _provider.LastQueuedFiles);
        }

        [Theory]
        [InlineData(QueueType.Next)]
        [InlineData(QueueType.Last)]
        [InlineData(QueueType.PlayNow)]
        [InlineData(QueueType.AddAndPlay)]
        public void QueueFiles_AllTypes_ReturnTrue(QueueType queueType)
        {
            var files = new[] { "/music/song.mp3" };

            var result = _provider.QueueFiles(queueType, files);

            Assert.True(result);
            Assert.Equal(queueType, _provider.LastQueueType);
        }

        [Fact]
        public void PlayAllLibrary_IncreasesCallCount()
        {
            _provider.PlayAllLibrary();

            Assert.Equal(1, _provider.PlayAllLibraryCallCount);
        }

        [Fact]
        public void PlayAllLibrary_WithShuffle_ReturnsTrue()
        {
            var result = _provider.PlayAllLibrary(shuffle: true);
            Assert.True(result);
        }

        #endregion

        #region Playlist Management Tests

        [Fact]
        public void PlayPlaylist_IncreasesCallCount()
        {
            _provider.PlayPlaylist("playlist://favorites");

            Assert.Equal(1, _provider.PlayPlaylistCallCount);
        }

        [Fact]
        public void GetPlaylists_ReturnsConfiguredPlaylists()
        {
            var playlists = _provider.GetPlaylists().ToList();

            Assert.Equal(2, playlists.Count);
            Assert.Contains(playlists, p => p.Name == "Favorites");
            Assert.Contains(playlists, p => p.Name == "Recently Played");
        }

        [Fact]
        public void GetPlaylists_CanBeModified()
        {
            _provider.Playlists.Clear();
            _provider.Playlists.Add(new Playlist { Name = "Custom", Url = "playlist://custom" });

            var playlists = _provider.GetPlaylists().ToList();

            Assert.Single(playlists);
            Assert.Equal("Custom", playlists[0].Name);
        }

        #endregion
    }
}
