using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Configuration;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Fixtures;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class NowPlayingCommandsTests
    {
        private const string TestConnectionId = "test-connection-123";

        private readonly Mock<ITrackDataProvider> _trackDataProvider;
        private readonly Mock<IPlaylistDataProvider> _playlistDataProvider;
        private readonly Mock<IPlayerDataProvider> _playerDataProvider;
        private readonly MockLogger _logger;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly Mock<LyricCoverModel> _lyricCoverModel;
        private readonly Mock<IBroadcaster> _broadcaster;
        private readonly Mock<IUserSettings> _userSettings;
        private readonly Mock<IProtocolCapabilities> _protocolCapabilities;
        private readonly NowPlayingCommands _sut;

        public NowPlayingCommandsTests()
        {
            _trackDataProvider = new Mock<ITrackDataProvider>();
            _playlistDataProvider = new Mock<IPlaylistDataProvider>();
            _playerDataProvider = new Mock<IPlayerDataProvider>();
            _logger = new MockLogger();
            _eventAggregator = new Mock<IEventAggregator>();
            _broadcaster = new Mock<IBroadcaster>();
            _userSettings = new Mock<IUserSettings>();
            _protocolCapabilities = new Mock<IProtocolCapabilities>();

            // Create LyricCoverModel mock with proper constructor args
            _lyricCoverModel = new Mock<LyricCoverModel>(_logger, _broadcaster.Object);

            _sut = new NowPlayingCommands(
                _trackDataProvider.Object,
                _playlistDataProvider.Object,
                _playerDataProvider.Object,
                _logger,
                _eventAggregator.Object,
                _lyricCoverModel.Object,
                _broadcaster.Object,
                _userSettings.Object,
                _protocolCapabilities.Object);
        }

        #region 2.1 Track Information

        [Fact]
        public void HandleTrackInfo_LegacyClient_ReturnsNowPlayingTrack()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPayloadObjects(TestConnectionId)).Returns(false);
            var trackV2 = new NowPlayingTrackV2 { Artist = "Artist", Album = "Album", Title = "Title", Year = "2023" };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(trackV2);
            var context = new TestCommandContext("trackinfo", null, TestConnectionId);

            // Act
            var result = _sut.HandleTrackInfo(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackInfo(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleTrackInfo_ModernClient_ReturnsNowPlayingTrackV2()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPayloadObjects(TestConnectionId)).Returns(true);
            var trackV2 = new NowPlayingTrackV2 { Artist = "Artist", Album = "Album", Title = "Title", Year = "2023" };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(trackV2);
            var context = new TestCommandContext("trackinfo", null, TestConnectionId);

            // Act
            var result = _sut.HandleTrackInfo(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackInfo(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleTrackDetails_ReturnsDetails()
        {
            // Arrange
            var details = new NowPlayingDetails { Bitrate = "320", Format = "MP3" };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackDetails()).Returns(details);
            var context = new TestCommandContext("trackdetails", null, TestConnectionId);

            // Act
            var result = _sut.HandleTrackDetails(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackDetails(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlaybackPosition_ReturnsPosition()
        {
            // Arrange
            var position = new PlaybackPosition { Current = 1000, Total = 240000 };
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(position);
            var context = new TestCommandContext("playbackposition", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlaybackPosition(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetPlaybackPosition(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleCover_LegacyClient_ReturnsRawCover()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPayloadObjects(TestConnectionId)).Returns(false);
            _lyricCoverModel.Setup(x => x.Cover).Returns("base64coverdata");
            var context = new TestCommandContext("cover", null, TestConnectionId);

            // Act
            var result = _sut.HandleCover(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleCover_ModernClient_ReturnsCoverPayload()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPayloadObjects(TestConnectionId)).Returns(true);
            _lyricCoverModel.Setup(x => x.Cover).Returns("base64coverdata");
            var context = new TestCommandContext("cover", null, TestConnectionId);

            // Act
            var result = _sut.HandleCover(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleLyrics_LegacyClient_ReturnsRawLyrics()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPayloadObjects(TestConnectionId)).Returns(false);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("Test lyrics");
            var context = new TestCommandContext("lyrics", null, TestConnectionId);

            // Act
            var result = _sut.HandleLyrics(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleLyrics_ModernClient_ReturnsLyricsPayload()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPayloadObjects(TestConnectionId)).Returns(true);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("Test lyrics");
            var context = new TestCommandContext("lyrics", null, TestConnectionId);

            // Act
            var result = _sut.HandleLyrics(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleLyrics_EmptyLyrics_ReturnsNotFoundMessage()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPayloadObjects(TestConnectionId)).Returns(false);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns(string.Empty);
            var context = new TestCommandContext("lyrics", null, TestConnectionId);

            // Act
            var result = _sut.HandleLyrics(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 2.2 Queue Management

        [Fact]
        public void HandleRemoveTrack_ValidIndex_RemovesTrack()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.RemoveFromNowPlayingList(5)).Returns(true);
            var context = new TestCommandContext("removetrack", 5, TestConnectionId);

            // Act
            var result = _sut.HandleRemoveTrack(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.RemoveFromNowPlayingList(5), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleRemoveTrack_NegativeIndex_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("removetrack", -1, TestConnectionId);

            // Act
            var result = _sut.HandleRemoveTrack(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.RemoveFromNowPlayingList(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void HandleRemoveTrack_InvalidData_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("removetrack", "invalid", TestConnectionId);

            // Act
            var result = _sut.HandleRemoveTrack(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.RemoveFromNowPlayingList(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void HandleRemoveTrack_ZeroIndex_RemovesTrack()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.RemoveFromNowPlayingList(0)).Returns(true);
            var context = new TestCommandContext("removetrack", 0, TestConnectionId);

            // Act
            var result = _sut.HandleRemoveTrack(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.RemoveFromNowPlayingList(0), Times.Once);
        }

        [Fact]
        public void HandleMoveTrack_ValidPositions_MovesTrack()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.MoveNowPlayingTrack(2, 5)).Returns(true);
            var data = JObject.FromObject(new { from = 2, to = 5 });
            var innerContext = new TestCommandContext("movetrack", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.MoveTrackRequest>(innerContext);

            // Act
            var result = _sut.HandleMoveTrack(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.MoveNowPlayingTrack(2, 5), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleMoveTrack_NegativeFrom_ReturnsFalse()
        {
            // Arrange
            var data = JObject.FromObject(new { from = -1, to = 5 });
            var innerContext = new TestCommandContext("movetrack", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.MoveTrackRequest>(innerContext);

            // Act
            var result = _sut.HandleMoveTrack(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.MoveNowPlayingTrack(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void HandleMoveTrack_NegativeTo_ReturnsFalse()
        {
            // Arrange
            var data = JObject.FromObject(new { from = 2, to = -1 });
            var innerContext = new TestCommandContext("movetrack", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.MoveTrackRequest>(innerContext);

            // Act
            var result = _sut.HandleMoveTrack(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.MoveNowPlayingTrack(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void HandleMoveTrack_MissingFrom_ReturnsFalse()
        {
            // Arrange
            var data = JObject.FromObject(new { to = 5 });
            var innerContext = new TestCommandContext("movetrack", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.MoveTrackRequest>(innerContext);

            // Act
            var result = _sut.HandleMoveTrack(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.MoveNowPlayingTrack(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void HandleMoveTrack_MissingTo_ReturnsFalse()
        {
            // Arrange
            var data = JObject.FromObject(new { from = 2 });
            var innerContext = new TestCommandContext("movetrack", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.MoveTrackRequest>(innerContext);

            // Act
            var result = _sut.HandleMoveTrack(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.MoveNowPlayingTrack(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void HandleMoveTrack_InvalidDataFormat_ReturnsFalse()
        {
            // Arrange
            var innerContext = new TestCommandContext("movetrack", "invalid", TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.MoveTrackRequest>(innerContext);

            // Act
            var result = _sut.HandleMoveTrack(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.MoveNowPlayingTrack(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void HandleQueue_ValidData_QueuesFiles()
        {
            // Arrange
            var request = new QueueRequest { Queue = "next", Data = new System.Collections.Generic.List<string> { "file1.mp3", "file2.mp3" } };
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Next, It.IsAny<string[]>(), null)).Returns(true);
            var context = new TestTypedCommandContext<QueueRequest>("queue", request, TestConnectionId);

            // Act
            var result = _sut.HandleQueue(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.QueueFiles(QueueType.Next, It.IsAny<string[]>(), null), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleQueue_EmptyData_ReturnsFalse()
        {
            // Arrange
            var request = new QueueRequest { Queue = "next", Data = new System.Collections.Generic.List<string>() };
            var context = new TestTypedCommandContext<QueueRequest>("queue", request, TestConnectionId);

            // Act
            var result = _sut.HandleQueue(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.QueueFiles(It.IsAny<QueueType>(), It.IsAny<string[]>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandleQueue_InvalidDataFormat_ReturnsFalse()
        {
            // Arrange - null typed data simulates invalid format
            var context = new TestTypedCommandContext<QueueRequest>("queue", null, TestConnectionId);

            // Act
            var result = _sut.HandleQueue(context);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HandleQueue_QueueTypeLast_UsesLastQueueType()
        {
            // Arrange
            var request = new QueueRequest { Queue = "last", Data = new System.Collections.Generic.List<string> { "file1.mp3" } };
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Last, It.IsAny<string[]>(), null)).Returns(true);
            var context = new TestTypedCommandContext<QueueRequest>("queue", request, TestConnectionId);

            // Act
            var result = _sut.HandleQueue(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.QueueFiles(QueueType.Last, It.IsAny<string[]>(), null), Times.Once);
        }

        [Fact]
        public void HandleQueue_QueueTypePlayNow_UsesPlayNowQueueType()
        {
            // Arrange
            var request = new QueueRequest { Queue = "playnow", Data = new System.Collections.Generic.List<string> { "file1.mp3" } };
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.PlayNow, It.IsAny<string[]>(), null)).Returns(true);
            var context = new TestTypedCommandContext<QueueRequest>("queue", request, TestConnectionId);

            // Act
            var result = _sut.HandleQueue(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.QueueFiles(QueueType.PlayNow, It.IsAny<string[]>(), null), Times.Once);
        }

        #endregion

        #region 2.3 Android Index Handling - CRITICAL

        [Fact]
        public void HandleNowPlayingListPlay_Android_AdjustsIndex()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.GetClientPlatform(TestConnectionId)).Returns(ClientOS.Android);
            _playlistDataProvider.Setup(x => x.PlayNowPlayingByIndex(4)).Returns(true);
            var context = new TestCommandContext("nowplayinglistplay", 5, TestConnectionId);

            // Act
            var result = _sut.HandleNowPlayingListPlay(context);

            // Assert
            result.Should().BeTrue();
            // Android sends 1-based index, should be converted to 0-based (5-1=4)
            _playlistDataProvider.Verify(x => x.PlayNowPlayingByIndex(4), Times.Once);
        }

        [Fact]
        public void HandleNowPlayingListPlay_NonAndroid_UsesDirectIndex()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.GetClientPlatform(TestConnectionId)).Returns(ClientOS.iOS);
            _playlistDataProvider.Setup(x => x.PlayNowPlayingByIndex(5)).Returns(true);
            var context = new TestCommandContext("nowplayinglistplay", 5, TestConnectionId);

            // Act
            var result = _sut.HandleNowPlayingListPlay(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.PlayNowPlayingByIndex(5), Times.Once);
        }

        [Fact]
        public void HandleNowPlayingListPlay_InvalidIndex_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("nowplayinglistplay", "invalid", TestConnectionId);

            // Act
            var result = _sut.HandleNowPlayingListPlay(context);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HandleNowPlayingList_LegacyClient_UsesLegacyMethod()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(false);
            _trackDataProvider.Setup(x => x.GetNowPlayingListLegacy()).Returns(new System.Collections.Generic.List<NowPlayingListTrack>());
            var context = new TestTypedCommandContext<PaginationRequest>("nowplayinglist", null, TestConnectionId);

            // Act
            var result = _sut.HandleNowPlayingList(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetNowPlayingListLegacy(), Times.Once);
        }

        [Fact]
        public void HandleNowPlayingList_Android_UsesPageMethod()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(true);
            _protocolCapabilities.Setup(x => x.GetClientPlatform(TestConnectionId)).Returns(ClientOS.Android);
            var paginationRequest = new PaginationRequest { Offset = 0, Limit = 100 };
            _trackDataProvider.Setup(x => x.GetNowPlayingListPage(0, 100)).Returns(new System.Collections.Generic.List<NowPlaying>());
            var context = new TestTypedCommandContext<PaginationRequest>("nowplayinglist", paginationRequest, TestConnectionId);

            // Act
            var result = _sut.HandleNowPlayingList(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetNowPlayingListPage(0, 100), Times.Once);
        }

        [Fact]
        public void HandleNowPlayingList_NonAndroid_UsesOrderedMethod()
        {
            // Arrange
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(true);
            _protocolCapabilities.Setup(x => x.GetClientPlatform(TestConnectionId)).Returns(ClientOS.iOS);
            var paginationRequest = new PaginationRequest { Offset = 0, Limit = 100 };
            _trackDataProvider.Setup(x => x.GetNowPlayingListOrdered(0, 100)).Returns(new System.Collections.Generic.List<NowPlaying>());
            var context = new TestTypedCommandContext<PaginationRequest>("nowplayinglist", paginationRequest, TestConnectionId);

            // Act
            var result = _sut.HandleNowPlayingList(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetNowPlayingListOrdered(0, 100), Times.Once);
        }

        #endregion

        #region 2.4 Search and Navigation

        [Fact]
        public void HandleSearch_ValidQuery_PlaysMatchingTrack()
        {
            // Arrange
            _userSettings.Setup(x => x.Source).Returns(SearchSource.Library);
            _trackDataProvider.Setup(x => x.SearchNowPlayingList("test song", SearchSource.Library)).Returns("track.mp3");
            _playlistDataProvider.Setup(x => x.PlayNowPlayingTrack("track.mp3")).Returns(true);
            var context = new TestCommandContext("search", "test song", TestConnectionId);

            // Act
            var result = _sut.HandleSearch(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.PlayNowPlayingTrack("track.mp3"), Times.Once);
        }

        [Fact]
        public void HandleSearch_NoMatch_ReturnsTrue()
        {
            // Arrange
            _userSettings.Setup(x => x.Source).Returns(SearchSource.Library);
            _trackDataProvider.Setup(x => x.SearchNowPlayingList("nonexistent", SearchSource.Library)).Returns(string.Empty);
            var context = new TestCommandContext("search", "nonexistent", TestConnectionId);

            // Act
            var result = _sut.HandleSearch(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.PlayNowPlayingTrack(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandleSearch_EmptyQuery_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("search", string.Empty, TestConnectionId);

            // Act
            var result = _sut.HandleSearch(context);

            // Assert
            result.Should().BeFalse();
            _trackDataProvider.Verify(x => x.SearchNowPlayingList(It.IsAny<string>(), It.IsAny<SearchSource>()), Times.Never);
        }

        [Fact]
        public void HandleSearch_WhitespaceQuery_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("search", "   ", TestConnectionId);

            // Act
            var result = _sut.HandleSearch(context);

            // Assert
            result.Should().BeFalse();
            _trackDataProvider.Verify(x => x.SearchNowPlayingList(It.IsAny<string>(), It.IsAny<SearchSource>()), Times.Never);
        }

        [Fact]
        public void HandleSearch_NullQuery_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("search", null, TestConnectionId);

            // Act
            var result = _sut.HandleSearch(context);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region 2.5 Tag Editing

        [Fact]
        public void HandleTagChange_ValidTag_UpdatesAndCommits()
        {
            // Arrange
            var data = JObject.FromObject(new { tag = "Title", value = "New Title" });
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns("track.mp3");
            _trackDataProvider.Setup(x => x.SetTrackTag("track.mp3", "Title", "New Title")).Returns(true);
            _trackDataProvider.Setup(x => x.CommitTrackTags("track.mp3")).Returns(true);
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackDetails()).Returns(new NowPlayingDetails());
            var innerContext = new TestCommandContext("tagchange", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.TagChangeRequest>(innerContext);

            // Act
            var result = _sut.HandleTagChange(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetTrackTag("track.mp3", "Title", "New Title"), Times.Once);
            _trackDataProvider.Verify(x => x.CommitTrackTags("track.mp3"), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleTagChange_MissingTagName_ReturnsFalse()
        {
            // Arrange
            var data = JObject.FromObject(new { value = "New Title" });
            var innerContext = new TestCommandContext("tagchange", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.TagChangeRequest>(innerContext);

            // Act
            var result = _sut.HandleTagChange(context);

            // Assert
            result.Should().BeFalse();
            _trackDataProvider.Verify(x => x.SetTrackTag(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandleTagChange_NoCurrentTrack_ReturnsFalse()
        {
            // Arrange
            var data = JObject.FromObject(new { tag = "Title", value = "New Title" });
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns(string.Empty);
            var innerContext = new TestCommandContext("tagchange", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.TagChangeRequest>(innerContext);

            // Act
            var result = _sut.HandleTagChange(context);

            // Assert
            result.Should().BeFalse();
            _trackDataProvider.Verify(x => x.SetTrackTag(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandleTagChange_SetTagFails_ReturnsFalse()
        {
            // Arrange
            var data = JObject.FromObject(new { tag = "Title", value = "New Title" });
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns("track.mp3");
            _trackDataProvider.Setup(x => x.SetTrackTag("track.mp3", "Title", "New Title")).Returns(false);
            var innerContext = new TestCommandContext("tagchange", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.TagChangeRequest>(innerContext);

            // Act
            var result = _sut.HandleTagChange(context);

            // Assert
            result.Should().BeFalse();
            _trackDataProvider.Verify(x => x.CommitTrackTags(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandleTagChange_CommitFails_ReturnsFalse()
        {
            // Arrange
            var data = JObject.FromObject(new { tag = "Title", value = "New Title" });
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns("track.mp3");
            _trackDataProvider.Setup(x => x.SetTrackTag("track.mp3", "Title", "New Title")).Returns(true);
            _trackDataProvider.Setup(x => x.CommitTrackTags("track.mp3")).Returns(false);
            var innerContext = new TestCommandContext("tagchange", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.TagChangeRequest>(innerContext);

            // Act
            var result = _sut.HandleTagChange(context);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HandleTagChange_NullValue_SetsEmptyString()
        {
            // Arrange
            var data = JObject.FromObject(new { tag = "Title" });
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns("track.mp3");
            _trackDataProvider.Setup(x => x.SetTrackTag("track.mp3", "Title", string.Empty)).Returns(true);
            _trackDataProvider.Setup(x => x.CommitTrackTags("track.mp3")).Returns(true);
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackDetails()).Returns(new NowPlayingDetails());
            var innerContext = new TestCommandContext("tagchange", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.TagChangeRequest>(innerContext);

            // Act
            var result = _sut.HandleTagChange(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetTrackTag("track.mp3", "Title", string.Empty), Times.Once);
        }

        [Fact]
        public void HandleTagChange_InvalidDataFormat_ReturnsFalse()
        {
            // Arrange
            var innerContext = new TestCommandContext("tagchange", "invalid", TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.TagChangeRequest>(innerContext);

            // Act
            var result = _sut.HandleTagChange(context);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region 2.6 Rating and LastFM

        [Fact]
        public void HandleRating_SetRating_SetsAndReturns()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.SetNowPlayingRating("5")).Returns(true);
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("5");
            var context = new TestCommandContext("rating", "5", TestConnectionId);

            // Act
            var result = _sut.HandleRating(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetNowPlayingRating("5"), Times.Once);
            _trackDataProvider.Verify(x => x.GetNowPlayingRating(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleRating_NoData_ReturnsCurrentRating()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");
            var context = new TestCommandContext("rating", null, TestConnectionId);

            // Act
            var result = _sut.HandleRating(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetNowPlayingRating(It.IsAny<string>()), Times.Never);
            _trackDataProvider.Verify(x => x.GetNowPlayingRating(), Times.Once);
        }

        [Fact]
        public void HandleLastfmLoveRating_Toggle_CyclesState()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Love)).Returns(true);
            var context = new TestCommandContext("lfmrating", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleLastfmLoveRating(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Love), Times.Once);
        }

        [Fact]
        public void HandleLastfmLoveRating_ToggleFromLove_SetsNormal()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Love);
            _trackDataProvider.Setup(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Normal)).Returns(true);
            var context = new TestCommandContext("lfmrating", "toggle", TestConnectionId);

            // Act
            var result = _sut.HandleLastfmLoveRating(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Normal), Times.Once);
        }

        [Fact]
        public void HandleLastfmLoveRating_Love_SetsLove()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Love)).Returns(true);
            var context = new TestCommandContext("lfmrating", "love", TestConnectionId);

            // Act
            var result = _sut.HandleLastfmLoveRating(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Love), Times.Once);
        }

        [Fact]
        public void HandleLastfmLoveRating_Ban_SetsBan()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Ban)).Returns(true);
            var context = new TestCommandContext("lfmrating", "ban", TestConnectionId);

            // Act
            var result = _sut.HandleLastfmLoveRating(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Ban), Times.Once);
        }

        [Fact]
        public void HandleLastfmLoveRating_UnknownAction_ReturnsCurrentStatus()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Love);
            var context = new TestCommandContext("lfmrating", "unknown", TestConnectionId);

            // Act
            var result = _sut.HandleLastfmLoveRating(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(It.IsAny<LastfmStatus>()), Times.Never);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleLastfmLoveRating_NoData_ReturnsCurrentStatus()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            var context = new TestCommandContext("lfmrating", null, TestConnectionId);

            // Act
            var result = _sut.HandleLastfmLoveRating(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(It.IsAny<LastfmStatus>()), Times.Never);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 2.7 Initialization

        [Fact]
        public void HandleInit_BroadcastsAllState()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(new NowPlayingTrackV2());
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns("cover");
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("lyrics");
            _protocolCapabilities.Setup(x => x.SupportsFullPlayerStatus(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetPlayerStatus(false)).Returns(new PlayerStatus());
            var context = new TestCommandContext("init", null, TestConnectionId);

            // Act
            var result = _sut.HandleInit(context);

            // Assert
            result.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackInfo(), Times.Once);
            _trackDataProvider.Verify(x => x.GetNowPlayingRating(), Times.Once);
            _trackDataProvider.Verify(x => x.GetNowPlayingLastfmStatus(), Times.Once);
            _playerDataProvider.Verify(x => x.GetPlayerStatus(false), Times.Once);
            _broadcaster.Verify(x => x.BroadcastCover("cover"), Times.Once);
            _broadcaster.Verify(x => x.BroadcastLyrics("lyrics"), Times.Once);
        }

        [Fact]
        public void HandleInit_NoCover_SkipsCoverBroadcast()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(new NowPlayingTrackV2());
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns(string.Empty);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("lyrics");
            _protocolCapabilities.Setup(x => x.SupportsFullPlayerStatus(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetPlayerStatus(false)).Returns(new PlayerStatus());
            var context = new TestCommandContext("init", null, TestConnectionId);

            // Act
            var result = _sut.HandleInit(context);

            // Assert
            result.Should().BeTrue();
            _broadcaster.Verify(x => x.BroadcastCover(It.IsAny<string>()), Times.Never);
            _broadcaster.Verify(x => x.BroadcastLyrics("lyrics"), Times.Once);
        }

        [Fact]
        public void HandleInit_NoLyrics_SkipsLyricsBroadcast()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(new NowPlayingTrackV2());
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns("cover");
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns(string.Empty);
            _protocolCapabilities.Setup(x => x.SupportsFullPlayerStatus(TestConnectionId)).Returns(true);
            _playerDataProvider.Setup(x => x.GetPlayerStatus(false)).Returns(new PlayerStatus());
            var context = new TestCommandContext("init", null, TestConnectionId);

            // Act
            var result = _sut.HandleInit(context);

            // Assert
            result.Should().BeTrue();
            _broadcaster.Verify(x => x.BroadcastCover("cover"), Times.Once);
            _broadcaster.Verify(x => x.BroadcastLyrics(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandleInit_LegacyClient_UsesLegacyPlayerStatus()
        {
            // Arrange
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(new NowPlayingTrackV2());
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns(string.Empty);
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns(string.Empty);
            _protocolCapabilities.Setup(x => x.SupportsFullPlayerStatus(TestConnectionId)).Returns(false);
            _playerDataProvider.Setup(x => x.GetPlayerStatus(true)).Returns(new PlayerStatus());
            var context = new TestCommandContext("init", null, TestConnectionId);

            // Act
            var result = _sut.HandleInit(context);

            // Assert
            result.Should().BeTrue();
            _playerDataProvider.Verify(x => x.GetPlayerStatus(true), Times.Once);
        }

        #endregion
    }
}
