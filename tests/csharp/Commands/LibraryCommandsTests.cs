using System.Collections.Generic;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Models.Responses;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Media;
using MusicBeeRemote.Core.Tests.Fixtures;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class LibraryCommandsTests
    {
        private const string TestConnectionId = "test-connection-123";

        private readonly Mock<ILibraryDataProvider> _libraryDataProvider;
        private readonly Mock<IPlaylistDataProvider> _playlistDataProvider;
        private readonly Mock<ICoverService> _coverService;
        private readonly MockLogger _logger;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly Mock<IUserSettings> _userSettings;
        private readonly LibraryCommands _sut;

        public LibraryCommandsTests()
        {
            _libraryDataProvider = new Mock<ILibraryDataProvider>();
            _playlistDataProvider = new Mock<IPlaylistDataProvider>();
            _coverService = new Mock<ICoverService>();
            _logger = new MockLogger();
            _eventAggregator = new Mock<IEventAggregator>();
            _userSettings = new Mock<IUserSettings>();

            // Default user settings
            _userSettings.Setup(x => x.Source).Returns(SearchSource.Library);

            _sut = new LibraryCommands(
                _libraryDataProvider.Object,
                _playlistDataProvider.Object,
                _coverService.Object,
                _logger,
                _eventAggregator.Object,
                _userSettings.Object);
        }

        #region 2.1 Radio Stations

        [Fact]
        public void HandleRadioStations_ReturnsStations_PublishesResponse()
        {
            // Arrange
            var stations = new List<RadioStation>
            {
                new RadioStation { Name = "Station 1", Url = "http://station1.com" },
                new RadioStation { Name = "Station 2", Url = "http://station2.com" }
            };
            _libraryDataProvider.Setup(x => x.GetRadioStations(0, 4000)).Returns(stations);
            var context = new TestTypedCommandContext<PaginationRequest>("radiostations", null, TestConnectionId);

            // Act
            var result = _sut.HandleRadioStations(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.GetRadioStations(0, 4000), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleRadioStations_WithPagination_UsesOffsetAndLimit()
        {
            // Arrange
            var stations = new List<RadioStation>();
            _libraryDataProvider.Setup(x => x.GetRadioStations(10, 20)).Returns(stations);
            var paginationRequest = new PaginationRequest { Offset = 10, Limit = 20 };
            var context = new TestTypedCommandContext<PaginationRequest>("radiostations", paginationRequest, TestConnectionId);

            // Act
            var result = _sut.HandleRadioStations(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.GetRadioStations(10, 20), Times.Once);
        }

        #endregion

        #region 2.2 Library Search

        [Fact]
        public void HandleSearchTitle_WithQuery_SearchesAndPublishes()
        {
            // Arrange
            var tracks = new List<Track> { new Track("Artist", "Test Song", 1, "/path/song.mp3") };
            _libraryDataProvider.Setup(x => x.SearchTracks("test", SearchSource.Library)).Returns(tracks);
            var context = new TestCommandContext("libsearchtitle", "test", TestConnectionId);

            // Act
            var result = _sut.HandleSearchTitle(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.SearchTracks("test", SearchSource.Library), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleSearchGenre_WithQuery_SearchesAndPublishes()
        {
            // Arrange
            var genres = new List<GenreData> { new GenreData("Rock", 50) };
            _libraryDataProvider.Setup(x => x.SearchGenres("rock", SearchSource.Library)).Returns(genres);
            var context = new TestCommandContext("libsearchgenre", "rock", TestConnectionId);

            // Act
            var result = _sut.HandleSearchGenre(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.SearchGenres("rock", SearchSource.Library), Times.Once);
        }

        [Fact]
        public void HandleSearchArtist_WithQuery_SearchesAndPublishes()
        {
            // Arrange
            var artists = new List<ArtistData> { new ArtistData("Beatles", 100) };
            _libraryDataProvider.Setup(x => x.SearchArtists("beatles", SearchSource.Library)).Returns(artists);
            var context = new TestCommandContext("libsearchartist", "beatles", TestConnectionId);

            // Act
            var result = _sut.HandleSearchArtist(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.SearchArtists("beatles", SearchSource.Library), Times.Once);
        }

        [Fact]
        public void HandleSearchAlbum_WithQuery_SearchesAndPublishes()
        {
            // Arrange
            var albums = new List<AlbumData> { new AlbumData("Beatles", "Abbey Road") };
            _libraryDataProvider.Setup(x => x.SearchAlbums("abbey", SearchSource.Library)).Returns(albums);
            var context = new TestCommandContext("libsearchalbum", "abbey", TestConnectionId);

            // Act
            var result = _sut.HandleSearchAlbum(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.SearchAlbums("abbey", SearchSource.Library), Times.Once);
        }

        [Fact]
        public void HandleSearchTitle_EmptyQuery_StillSearches()
        {
            // Arrange
            var tracks = new List<Track>();
            _libraryDataProvider.Setup(x => x.SearchTracks(string.Empty, SearchSource.Library)).Returns(tracks);
            var context = new TestCommandContext("libsearchtitle", string.Empty, TestConnectionId);

            // Act
            var result = _sut.HandleSearchTitle(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.SearchTracks(string.Empty, SearchSource.Library), Times.Once);
        }

        #endregion

        #region 2.3 Library Queue

        [Fact]
        public void HandleQueueTrack_ValidData_QueuesAndReturnsTrue()
        {
            // Arrange
            var queueData = JObject.FromObject(new { type = "next", query = "Test Song" });
            _libraryDataProvider.Setup(x => x.GetFileUrlsByMetaTag(MetaTag.Title, "Test Song", SearchSource.Library))
                .Returns(new[] { "/path/song.mp3" });
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Next, It.IsAny<string[]>(), It.IsAny<string>()))
                .Returns(true);
            var innerContext = new TestCommandContext("libqueuetrack", queueData, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.SearchRequest>(innerContext);

            // Act
            var result = _sut.HandleQueueTrack(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.QueueFiles(QueueType.Next, It.IsAny<string[]>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void HandleQueueTrack_PlayNowType_UsesQueryDirectly()
        {
            // Arrange
            var queueData = JObject.FromObject(new { type = "now", query = "/path/to/song.mp3" });
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.PlayNow, It.IsAny<string[]>(), It.IsAny<string>()))
                .Returns(true);
            var innerContext = new TestCommandContext("libqueuetrack", queueData, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.SearchRequest>(innerContext);

            // Act
            var result = _sut.HandleQueueTrack(context);

            // Assert
            result.Should().BeTrue();
            // For PlayNow with Title tag, the query is used directly as the track path
            _playlistDataProvider.Verify(x => x.QueueFiles(QueueType.PlayNow,
                It.Is<string[]>(arr => arr.Length == 1 && arr[0] == "/path/to/song.mp3"),
                "/path/to/song.mp3"), Times.Once);
        }

        [Fact]
        public void HandleQueueGenre_ValidData_QueuesGenreTracks()
        {
            // Arrange
            var queueData = JObject.FromObject(new { type = "last", query = "Rock" });
            _libraryDataProvider.Setup(x => x.GetFileUrlsByMetaTag(MetaTag.Genre, "Rock", SearchSource.Library))
                .Returns(new[] { "/path/rock1.mp3", "/path/rock2.mp3" });
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Last, It.IsAny<string[]>(), It.IsAny<string>()))
                .Returns(true);
            var innerContext = new TestCommandContext("libqueuegenre", queueData, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.SearchRequest>(innerContext);

            // Act
            var result = _sut.HandleQueueGenre(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.GetFileUrlsByMetaTag(MetaTag.Genre, "Rock", SearchSource.Library), Times.Once);
        }

        [Fact]
        public void HandleQueueArtist_ValidData_QueuesArtistTracks()
        {
            // Arrange
            var queueData = JObject.FromObject(new { type = "next", query = "Beatles" });
            _libraryDataProvider.Setup(x => x.GetFileUrlsByMetaTag(MetaTag.Artist, "Beatles", SearchSource.Library))
                .Returns(new[] { "/path/beatles1.mp3" });
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Next, It.IsAny<string[]>(), It.IsAny<string>()))
                .Returns(true);
            var innerContext = new TestCommandContext("libqueueartist", queueData, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.SearchRequest>(innerContext);

            // Act
            var result = _sut.HandleQueueArtist(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.GetFileUrlsByMetaTag(MetaTag.Artist, "Beatles", SearchSource.Library), Times.Once);
        }

        [Fact]
        public void HandleQueueAlbum_ValidData_QueuesAlbumTracks()
        {
            // Arrange
            var queueData = JObject.FromObject(new { type = "next", query = "Abbey Road" });
            _libraryDataProvider.Setup(x => x.GetFileUrlsByMetaTag(MetaTag.Album, "Abbey Road", SearchSource.Library))
                .Returns(new[] { "/path/track1.mp3", "/path/track2.mp3" });
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Next, It.IsAny<string[]>(), It.IsAny<string>()))
                .Returns(true);
            var innerContext = new TestCommandContext("libqueuealbum", queueData, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.SearchRequest>(innerContext);

            // Act
            var result = _sut.HandleQueueAlbum(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.GetFileUrlsByMetaTag(MetaTag.Album, "Abbey Road", SearchSource.Library), Times.Once);
        }

        [Fact]
        public void HandleQueueTrack_InvalidDataFormat_ReturnsFalse()
        {
            // Arrange
            var innerContext = new TestCommandContext("libqueuetrack", "invalid string data", TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.SearchRequest>(innerContext);

            // Act
            var result = _sut.HandleQueueTrack(context);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region 2.4 Library Browse

        [Fact]
        public void HandleBrowseGenres_ReturnsGenres_PublishesResponse()
        {
            // Arrange
            var genres = new List<GenreData>
            {
                new GenreData("Rock", 100),
                new GenreData("Pop", 80)
            };
            _libraryDataProvider.Setup(x => x.BrowseGenres(0, 4000)).Returns(genres);
            var context = new TestTypedCommandContext<PaginationRequest>("libbrowsegenres", null, TestConnectionId);

            // Act
            var result = _sut.HandleBrowseGenres(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.BrowseGenres(0, 4000), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleBrowseArtists_ReturnsArtists_PublishesResponse()
        {
            // Arrange
            var artists = new List<ArtistData> { new ArtistData("Test Artist", 50) };
            _libraryDataProvider.Setup(x => x.BrowseArtists(0, 4000, false)).Returns(artists);
            var innerContext = new TestCommandContext("libbrowseartists", null, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.BrowseArtistsRequest>(innerContext);

            // Act
            var result = _sut.HandleBrowseArtists(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.BrowseArtists(0, 4000, false), Times.Once);
        }

        [Fact]
        public void HandleBrowseArtists_WithAlbumArtistsFlag_UsesFlag()
        {
            // Arrange
            var artists = new List<ArtistData> { new ArtistData("Album Artist", 30) };
            _libraryDataProvider.Setup(x => x.BrowseArtists(0, 4000, true)).Returns(artists);
            var data = JObject.FromObject(new { album_artists = true });
            var innerContext = new TestCommandContext("libbrowseartists", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.BrowseArtistsRequest>(innerContext);

            // Act
            var result = _sut.HandleBrowseArtists(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.BrowseArtists(0, 4000, true), Times.Once);
        }

        [Fact]
        public void HandleBrowseAlbums_ReturnsAlbums_PublishesResponse()
        {
            // Arrange
            var albums = new List<AlbumData> { new AlbumData("Artist", "Album") };
            _libraryDataProvider.Setup(x => x.BrowseAlbums(0, 4000)).Returns(albums);
            var context = new TestTypedCommandContext<PaginationRequest>("libbrowsealbums", null, TestConnectionId);

            // Act
            var result = _sut.HandleBrowseAlbums(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.BrowseAlbums(0, 4000), Times.Once);
        }

        [Fact]
        public void HandleBrowseTracks_ReturnsTracks_PublishesResponse()
        {
            // Arrange
            var tracks = new List<Track> { new Track("Artist", "Track", 1, "/path/track.mp3") };
            _libraryDataProvider.Setup(x => x.BrowseTracks(0, 4000)).Returns(tracks);
            var context = new TestTypedCommandContext<PaginationRequest>("libbrowsetracks", null, TestConnectionId);

            // Act
            var result = _sut.HandleBrowseTracks(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.BrowseTracks(0, 4000), Times.Once);
        }

        [Fact]
        public void HandleBrowseGenres_WithPagination_UsesOffsetAndLimit()
        {
            // Arrange
            var genres = new List<GenreData>();
            _libraryDataProvider.Setup(x => x.BrowseGenres(5, 10)).Returns(genres);
            var paginationRequest = new PaginationRequest { Offset = 5, Limit = 10 };
            var context = new TestTypedCommandContext<PaginationRequest>("libbrowsegenres", paginationRequest, TestConnectionId);

            // Act
            var result = _sut.HandleBrowseGenres(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.BrowseGenres(5, 10), Times.Once);
        }

        #endregion

        #region 2.5 Hierarchical Navigation

        [Fact]
        public void HandleAlbumTracks_ValidAlbum_ReturnsTracks()
        {
            // Arrange
            var tracks = new List<Track>
            {
                new Track("Artist", "Track 1", 1, "/path/track1.mp3"),
                new Track("Artist", "Track 2", 2, "/path/track2.mp3")
            };
            _libraryDataProvider.Setup(x => x.GetAlbumTracks("Test Album", SearchSource.Library)).Returns(tracks);
            var context = new TestCommandContext("libalbumtracks", "Test Album", TestConnectionId);

            // Act
            var result = _sut.HandleAlbumTracks(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.GetAlbumTracks("Test Album", SearchSource.Library), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandleAlbumTracks_EmptyAlbumName_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("libalbumtracks", string.Empty, TestConnectionId);

            // Act
            var result = _sut.HandleAlbumTracks(context);

            // Assert
            result.Should().BeFalse();
            _libraryDataProvider.Verify(x => x.GetAlbumTracks(It.IsAny<string>(), It.IsAny<SearchSource>()), Times.Never);
        }

        [Fact]
        public void HandleArtistAlbums_ValidArtist_ReturnsAlbums()
        {
            // Arrange
            var albums = new List<AlbumData>
            {
                new AlbumData("Test Artist", "Album 1"),
                new AlbumData("Test Artist", "Album 2")
            };
            _libraryDataProvider.Setup(x => x.GetArtistAlbums("Test Artist", SearchSource.Library)).Returns(albums);
            var context = new TestCommandContext("libartistalbums", "Test Artist", TestConnectionId);

            // Act
            var result = _sut.HandleArtistAlbums(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.GetArtistAlbums("Test Artist", SearchSource.Library), Times.Once);
        }

        [Fact]
        public void HandleArtistAlbums_EmptyArtistName_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("libartistalbums", string.Empty, TestConnectionId);

            // Act
            var result = _sut.HandleArtistAlbums(context);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HandleGenreArtists_ValidGenre_ReturnsArtists()
        {
            // Arrange
            var artists = new List<ArtistData> { new ArtistData("Rock Artist", 20) };
            _libraryDataProvider.Setup(x => x.GetGenreArtists("Rock", SearchSource.Library)).Returns(artists);
            var context = new TestCommandContext("libgenreartists", "Rock", TestConnectionId);

            // Act
            var result = _sut.HandleGenreArtists(context);

            // Assert
            result.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.GetGenreArtists("Rock", SearchSource.Library), Times.Once);
        }

        [Fact]
        public void HandleGenreArtists_EmptyGenreName_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("libgenreartists", string.Empty, TestConnectionId);

            // Act
            var result = _sut.HandleGenreArtists(context);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region 2.6 Play All

        [Fact]
        public void HandlePlayAll_NoShuffle_PlaysAll()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.PlayAllLibrary(false)).Returns(true);
            var context = new TestCommandContext("libplayall", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlayAll(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.PlayAllLibrary(false), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlayAll_WithShuffleTrue_PlaysAllShuffled()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.PlayAllLibrary(true)).Returns(true);
            var context = new TestCommandContext("libplayall", true, TestConnectionId);

            // Act
            var result = _sut.HandlePlayAll(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.PlayAllLibrary(true), Times.Once);
        }

        [Fact]
        public void HandlePlayAll_WithShuffleString_ParsesCorrectly()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.PlayAllLibrary(true)).Returns(true);
            var context = new TestCommandContext("libplayall", "true", TestConnectionId);

            // Act
            var result = _sut.HandlePlayAll(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.PlayAllLibrary(true), Times.Once);
        }

        [Fact]
        public void HandlePlayAll_ProviderFails_StillReturnsTrue()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.PlayAllLibrary(false)).Returns(false);
            var context = new TestCommandContext("libplayall", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlayAll(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 2.7 Album Cover

        [Fact]
        public void HandleAlbumCover_SingleCoverRequest_ReturnsCover()
        {
            // Arrange
            var coverPayload = new AlbumCoverPayload { Status = 200, Cover = "base64data" };
            _coverService.Setup(x => x.GetAlbumCover("Artist", "Album", "hash123")).Returns(coverPayload);
            var data = JObject.FromObject(new { artist = "Artist", album = "Album", hash = "hash123" });
            var innerContext = new TestCommandContext("libalbumcover", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.AlbumCoverRequest>(innerContext);

            // Act
            var result = _sut.HandleAlbumCover(context);

            // Assert
            result.Should().BeTrue();
            _coverService.Verify(x => x.GetAlbumCover("Artist", "Album", "hash123"), Times.Once);
        }

        [Fact]
        public void HandleAlbumCover_WithSizeParameter_UsesSizeMethod()
        {
            // Arrange
            var coverPayload = new AlbumCoverPayload { Status = 200, Cover = "resizeddata" };
            _coverService.Setup(x => x.GetCoverBySize("Artist", "Album", "300")).Returns(coverPayload);
            var data = JObject.FromObject(new { artist = "Artist", album = "Album", size = "300" });
            var innerContext = new TestCommandContext("libalbumcover", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.AlbumCoverRequest>(innerContext);

            // Act
            var result = _sut.HandleAlbumCover(context);

            // Assert
            result.Should().BeTrue();
            _coverService.Verify(x => x.GetCoverBySize("Artist", "Album", "300"), Times.Once);
        }

        [Fact]
        public void HandleAlbumCover_PaginatedRequest_ReturnsPage()
        {
            // Arrange
            var pageResult = new Page<AlbumCoverPayload>
            {
                Data = new List<AlbumCoverPayload> { new AlbumCoverPayload { Status = 200 } },
                Offset = 0,
                Limit = 10,
                Total = 1
            };
            _coverService.Setup(x => x.GetCoverPage(0, 10)).Returns(pageResult);
            var data = JObject.FromObject(new { offset = 0, limit = 10 });
            var innerContext = new TestCommandContext("libalbumcover", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.AlbumCoverRequest>(innerContext);

            // Act
            var result = _sut.HandleAlbumCover(context);

            // Assert
            result.Should().BeTrue();
            _coverService.Verify(x => x.GetCoverPage(0, 10), Times.Once);
        }

        [Fact]
        public void HandleAlbumCover_InvalidDataFormat_ReturnsFalse()
        {
            // Arrange
            var innerContext = new TestCommandContext("libalbumcover", "invalid string", TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.AlbumCoverRequest>(innerContext);

            // Act
            var result = _sut.HandleAlbumCover(context);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region 2.8 Cover Cache Status

        [Fact]
        public void HandleCoverCacheStatus_BroadcastsStatus()
        {
            // Arrange
            var context = new TestCommandContext("covercachestatus", null, TestConnectionId);

            // Act
            var result = _sut.HandleCoverCacheStatus(context);

            // Assert
            result.Should().BeTrue();
            _coverService.Verify(x => x.BroadcastCacheStatus(TestConnectionId), Times.Once);
        }

        #endregion
    }
}
