using System.Collections.Generic;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Models.Responses;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Protocol.Processing;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    /// Integration tests for library commands.
    /// Tests the full message flow: JSON → ProtocolHandler → CommandDispatcher → LibraryCommands → Response
    /// </summary>
    public class LibrarySearchIntegrationTests
    {
        private const string ClientId = "library-test-client";

        private readonly Authenticator _authenticator;
        private readonly DelegateCommandDispatcher _dispatcher;
        private readonly MockEventAggregator _eventAggregator;
        private readonly MockLogger _logger;
        private readonly Mock<ILibraryDataProvider> _libraryDataProvider;
        private readonly Mock<IPlaylistDataProvider> _playlistDataProvider;
        private readonly Mock<ICoverService> _coverService;
        private readonly Mock<IUserSettings> _userSettings;
        private readonly LibraryCommands _libraryCommands;
        private readonly ProtocolHandler _protocolHandler;

        public LibrarySearchIntegrationTests()
        {
            _logger = new MockLogger();
            _authenticator = new Authenticator();
            _eventAggregator = new MockEventAggregator();
            _dispatcher = new DelegateCommandDispatcher(_logger);
            _libraryDataProvider = new Mock<ILibraryDataProvider>();
            _playlistDataProvider = new Mock<IPlaylistDataProvider>();
            _coverService = new Mock<ICoverService>();
            _userSettings = new Mock<IUserSettings>();

            // Set default search source
            _userSettings.Setup(x => x.Source).Returns(SearchSource.Library);

            _libraryCommands = new LibraryCommands(
                _libraryDataProvider.Object,
                _playlistDataProvider.Object,
                _coverService.Object,
                _logger,
                _eventAggregator,
                _userSettings.Object);

            RegisterCommands();

            _protocolHandler = new ProtocolHandler(
                _logger,
                _authenticator,
                _eventAggregator,
                _dispatcher);
        }

        private void RegisterCommands()
        {
            var registrar = (ICommandRegistrar)_dispatcher;

            // Register system commands for handshake
            var systemUserSettings = new Mock<IUserSettings>();
            systemUserSettings.Setup(x => x.CurrentVersion).Returns("1.0.0");
            var systemCommands = new SystemCommands(_logger, _eventAggregator, _authenticator, systemUserSettings.Object);
            registrar.RegisterCommand(ProtocolConstants.Player, systemCommands.HandlePlayer);
            registrar.RegisterCommand<ProtocolHandshakeRequest>(
                ProtocolConstants.Protocol, systemCommands.HandleProtocol);

            // Register library commands - untyped handlers
            registrar.RegisterCommand(ProtocolConstants.LibrarySearchTitle, _libraryCommands.HandleSearchTitle);
            registrar.RegisterCommand(ProtocolConstants.LibrarySearchGenre, _libraryCommands.HandleSearchGenre);
            registrar.RegisterCommand(ProtocolConstants.LibrarySearchArtist, _libraryCommands.HandleSearchArtist);
            registrar.RegisterCommand(ProtocolConstants.LibrarySearchAlbum, _libraryCommands.HandleSearchAlbum);
            registrar.RegisterCommand(ProtocolConstants.LibraryPlayAll, _libraryCommands.HandlePlayAll);
            registrar.RegisterCommand(ProtocolConstants.LibraryAlbumTracks, _libraryCommands.HandleAlbumTracks);
            registrar.RegisterCommand(ProtocolConstants.LibraryArtistAlbums, _libraryCommands.HandleArtistAlbums);
            registrar.RegisterCommand(ProtocolConstants.LibraryGenreArtists, _libraryCommands.HandleGenreArtists);
            registrar.RegisterCommand(ProtocolConstants.LibraryCoverCacheBuildStatus, _libraryCommands.HandleCoverCacheStatus);

            // Register library commands - typed handlers
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.RadioStations, _libraryCommands.HandleRadioStations);
            registrar.RegisterCommand<SearchRequest>(ProtocolConstants.LibraryQueueTrack, _libraryCommands.HandleQueueTrack);
            registrar.RegisterCommand<SearchRequest>(ProtocolConstants.LibraryQueueGenre, _libraryCommands.HandleQueueGenre);
            registrar.RegisterCommand<SearchRequest>(ProtocolConstants.LibraryQueueArtist, _libraryCommands.HandleQueueArtist);
            registrar.RegisterCommand<SearchRequest>(ProtocolConstants.LibraryQueueAlbum, _libraryCommands.HandleQueueAlbum);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.LibraryBrowseGenres, _libraryCommands.HandleBrowseGenres);
            registrar.RegisterCommand<BrowseArtistsRequest>(ProtocolConstants.LibraryBrowseArtists, _libraryCommands.HandleBrowseArtists);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.LibraryBrowseAlbums, _libraryCommands.HandleBrowseAlbums);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.LibraryBrowseTracks, _libraryCommands.HandleBrowseTracks);
            registrar.RegisterCommand<AlbumCoverRequest>(ProtocolConstants.LibraryAlbumCover, _libraryCommands.HandleAlbumCover);
        }

        private void CompleteHandshake(int protocolVersion = 4)
        {
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage($"{{\"context\":\"protocol\",\"data\":{protocolVersion}}}", ClientId);
            _eventAggregator.Clear();
        }

        #region Search Tests

        [Fact]
        public void SearchTitle_Query_SearchesTracks()
        {
            // Arrange
            CompleteHandshake();
            var tracks = new List<Track> { new Track { Title = "Test Song", Artist = "Test Artist" } };
            _libraryDataProvider.Setup(x => x.SearchTracks("test", It.IsAny<SearchSource>())).Returns(tracks);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"librarysearchtitle\",\"data\":\"test\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.SearchTracks("test", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibrarySearchTitle);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("librarysearchtitle");

            // Verify JSON structure - search returns raw array
            var data = response["data"];
            data.Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            data.Should().HaveCount(1);
            data[0]["title"].ToString().Should().Be("Test Song");
            data[0]["artist"].ToString().Should().Be("Test Artist");
        }

        [Fact]
        public void SearchGenre_Query_SearchesGenres()
        {
            // Arrange
            CompleteHandshake();
            var genres = new List<GenreData> { new GenreData("Rock", 10) };
            _libraryDataProvider.Setup(x => x.SearchGenres("rock", It.IsAny<SearchSource>())).Returns(genres);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"librarysearchgenre\",\"data\":\"rock\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.SearchGenres("rock", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibrarySearchGenre);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("librarysearchgenre");

            // Verify JSON structure
            var data = response["data"];
            data.Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            data.Should().HaveCount(1);
            data[0]["genre"].ToString().Should().Be("Rock");
            data[0]["count"].ToObject<int>().Should().Be(10);
        }

        [Fact]
        public void SearchArtist_Query_SearchesArtists()
        {
            // Arrange
            CompleteHandshake();
            var artists = new List<ArtistData> { new ArtistData("The Beatles", 50) };
            _libraryDataProvider.Setup(x => x.SearchArtists("beatles", It.IsAny<SearchSource>())).Returns(artists);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"librarysearchartist\",\"data\":\"beatles\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.SearchArtists("beatles", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibrarySearchArtist);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("librarysearchartist");

            // Verify JSON structure
            var data = response["data"];
            data.Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            data.Should().HaveCount(1);
            data[0]["artist"].ToString().Should().Be("The Beatles");
            data[0]["count"].ToObject<int>().Should().Be(50);
        }

        [Fact]
        public void SearchAlbum_Query_SearchesAlbums()
        {
            // Arrange
            CompleteHandshake();
            var albums = new List<AlbumData> { new AlbumData("The Beatles", "Abbey Road") };
            _libraryDataProvider.Setup(x => x.SearchAlbums("abbey", It.IsAny<SearchSource>())).Returns(albums);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"librarysearchalbum\",\"data\":\"abbey\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.SearchAlbums("abbey", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibrarySearchAlbum);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("librarysearchalbum");

            // Verify JSON structure
            var data = response["data"];
            data.Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            data.Should().HaveCount(1);
            data[0]["artist"].ToString().Should().Be("The Beatles");
            data[0]["album"].ToString().Should().Be("Abbey Road");
        }

        #endregion

        #region Browse Tests

        [Fact]
        public void BrowseGenres_Query_ReturnsGenres()
        {
            // Arrange
            CompleteHandshake();
            var genres = new List<GenreData> { new GenreData("Rock", 100), new GenreData("Jazz", 50) };
            _libraryDataProvider.Setup(x => x.BrowseGenres(0, It.IsAny<int>())).Returns(genres);

            // Act
            var request = new { offset = 0, limit = 100 };
            var json = JsonConvert.SerializeObject(new { context = "browsegenres", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.BrowseGenres(0, 100), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryBrowseGenres);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("browsegenres");

            // V4 client gets paginated response
            var pageData = response["data"];
            pageData["total"].Should().NotBeNull();
            pageData["offset"].ToObject<int>().Should().Be(0);
            pageData["limit"].ToObject<int>().Should().Be(100);
            pageData["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            pageData["data"].Should().HaveCount(2);
            pageData["data"][0]["genre"].ToString().Should().Be("Rock");
            pageData["data"][1]["genre"].ToString().Should().Be("Jazz");
        }

        [Fact]
        public void BrowseArtists_Query_ReturnsArtists()
        {
            // Arrange
            CompleteHandshake();
            var artists = new List<ArtistData> { new ArtistData("Artist 1", 10) };
            _libraryDataProvider.Setup(x => x.BrowseArtists(0, It.IsAny<int>(), false)).Returns(artists);

            // Act
            var request = new { offset = 0, limit = 50 };
            var json = JsonConvert.SerializeObject(new { context = "browseartists", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.BrowseArtists(0, 50, false), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryBrowseArtists);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("browseartists");

            // V4 client gets paginated response
            var pageData = response["data"];
            pageData["offset"].ToObject<int>().Should().Be(0);
            pageData["limit"].ToObject<int>().Should().Be(50);
            pageData["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            pageData["data"].Should().HaveCount(1);
            pageData["data"][0]["artist"].ToString().Should().Be("Artist 1");
            pageData["data"][0]["count"].ToObject<int>().Should().Be(10);
        }

        [Fact]
        public void BrowseArtists_AlbumArtistsFlag_UsesCorrectFlag()
        {
            // Arrange
            CompleteHandshake();
            var artists = new List<ArtistData> { new ArtistData("Album Artist", 5) };
            _libraryDataProvider.Setup(x => x.BrowseArtists(0, It.IsAny<int>(), true)).Returns(artists);

            // Act
            var request = new { offset = 0, limit = 50, album_artists = true };
            var json = JsonConvert.SerializeObject(new { context = "browseartists", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.BrowseArtists(0, 50, true), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryBrowseArtists);
            response.Should().NotBeNull();
            var pageData = response["data"];
            pageData["data"][0]["artist"].ToString().Should().Be("Album Artist");
        }

        [Fact]
        public void BrowseAlbums_Query_ReturnsAlbums()
        {
            // Arrange
            CompleteHandshake();
            var albums = new List<AlbumData> { new AlbumData("Artist 1", "Album 1") };
            _libraryDataProvider.Setup(x => x.BrowseAlbums(0, It.IsAny<int>())).Returns(albums);

            // Act
            var request = new { offset = 0, limit = 25 };
            var json = JsonConvert.SerializeObject(new { context = "browsealbums", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.BrowseAlbums(0, 25), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryBrowseAlbums);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("browsealbums");

            // V4 client gets paginated response
            var pageData = response["data"];
            pageData["offset"].ToObject<int>().Should().Be(0);
            pageData["limit"].ToObject<int>().Should().Be(25);
            pageData["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            pageData["data"].Should().HaveCount(1);
            pageData["data"][0]["artist"].ToString().Should().Be("Artist 1");
            pageData["data"][0]["album"].ToString().Should().Be("Album 1");
        }

        [Fact]
        public void BrowseTracks_Query_ReturnsTracks()
        {
            // Arrange
            CompleteHandshake();
            var tracks = new List<Track> { new Track { Title = "Track 1", Artist = "Artist 1" } };
            _libraryDataProvider.Setup(x => x.BrowseTracks(0, It.IsAny<int>())).Returns(tracks);

            // Act
            var request = new { offset = 0, limit = 100 };
            var json = JsonConvert.SerializeObject(new { context = "browsetracks", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.BrowseTracks(0, 100), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryBrowseTracks);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("browsetracks");

            // V4 client gets paginated response
            var pageData = response["data"];
            pageData["offset"].ToObject<int>().Should().Be(0);
            pageData["limit"].ToObject<int>().Should().Be(100);
            pageData["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            pageData["data"].Should().HaveCount(1);
            pageData["data"][0]["title"].ToString().Should().Be("Track 1");
            pageData["data"][0]["artist"].ToString().Should().Be("Artist 1");
        }

        [Fact]
        public void BrowseGenres_WithPagination_UsesCorrectOffset()
        {
            // Arrange
            CompleteHandshake();
            var genres = new List<GenreData> { new GenreData("Genre", 5) };
            _libraryDataProvider.Setup(x => x.BrowseGenres(100, It.IsAny<int>())).Returns(genres);

            // Act
            var request = new { offset = 100, limit = 50 };
            var json = JsonConvert.SerializeObject(new { context = "browsegenres", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.BrowseGenres(100, 50), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryBrowseGenres);
            response.Should().NotBeNull();
            var pageData = response["data"];
            pageData["offset"].ToObject<int>().Should().Be(100);
            pageData["limit"].ToObject<int>().Should().Be(50);
        }

        #endregion

        #region Queue Tests

        [Fact]
        public void QueueTrack_Query_QueuesTrack()
        {
            // Arrange
            CompleteHandshake();
            var files = new[] { "track.mp3" };
            _libraryDataProvider.Setup(x => x.GetFileUrlsByMetaTag(MetaTag.Title, "test song", It.IsAny<SearchSource>())).Returns(files);
            _playlistDataProvider.Setup(x => x.QueueFiles(It.IsAny<QueueType>(), It.IsAny<string[]>(), It.IsAny<string>())).Returns(true);

            // Act
            var request = new { type = "title", query = "test song" };
            var json = JsonConvert.SerializeObject(new { context = "libraryqueuetrack", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetFileUrlsByMetaTag(MetaTag.Title, "test song", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryQueueTrack);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryqueuetrack");
            response["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        [Fact]
        public void QueueGenre_Query_QueuesGenreTracks()
        {
            // Arrange
            CompleteHandshake();
            var files = new[] { "track1.mp3", "track2.mp3" };
            _libraryDataProvider.Setup(x => x.GetFileUrlsByMetaTag(MetaTag.Genre, "Rock", It.IsAny<SearchSource>())).Returns(files);
            _playlistDataProvider.Setup(x => x.QueueFiles(It.IsAny<QueueType>(), It.IsAny<string[]>(), It.IsAny<string>())).Returns(true);

            // Act
            var request = new { type = "genre", query = "Rock" };
            var json = JsonConvert.SerializeObject(new { context = "libraryqueuegenre", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetFileUrlsByMetaTag(MetaTag.Genre, "Rock", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryQueueGenre);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryqueuegenre");
            response["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        [Fact]
        public void QueueArtist_Query_QueuesArtistTracks()
        {
            // Arrange
            CompleteHandshake();
            var files = new[] { "artist_track.mp3" };
            _libraryDataProvider.Setup(x => x.GetFileUrlsByMetaTag(MetaTag.Artist, "Beatles", It.IsAny<SearchSource>())).Returns(files);
            _playlistDataProvider.Setup(x => x.QueueFiles(It.IsAny<QueueType>(), It.IsAny<string[]>(), It.IsAny<string>())).Returns(true);

            // Act
            var request = new { type = "artist", query = "Beatles" };
            var json = JsonConvert.SerializeObject(new { context = "libraryqueueartist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetFileUrlsByMetaTag(MetaTag.Artist, "Beatles", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryQueueArtist);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryqueueartist");
            response["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        [Fact]
        public void QueueAlbum_Query_QueuesAlbumTracks()
        {
            // Arrange
            CompleteHandshake();
            var files = new[] { "album_track1.mp3", "album_track2.mp3" };
            _libraryDataProvider.Setup(x => x.GetFileUrlsByMetaTag(MetaTag.Album, "Abbey Road", It.IsAny<SearchSource>())).Returns(files);
            _playlistDataProvider.Setup(x => x.QueueFiles(It.IsAny<QueueType>(), It.IsAny<string[]>(), It.IsAny<string>())).Returns(true);

            // Act
            var request = new { type = "album", query = "Abbey Road" };
            var json = JsonConvert.SerializeObject(new { context = "libraryqueuealbum", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetFileUrlsByMetaTag(MetaTag.Album, "Abbey Road", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryQueueAlbum);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryqueuealbum");
            response["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        #endregion

        #region PlayAll Tests

        [Fact]
        public void PlayAll_WithShuffleTrue_PlaysLibraryShuffled()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.PlayAllLibrary(true)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"libraryplayall\",\"data\":true}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.PlayAllLibrary(true), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryPlayAll);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryplayall");
            response["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        [Fact]
        public void PlayAll_WithShuffleFalse_PlaysLibraryInOrder()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.PlayAllLibrary(false)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"libraryplayall\",\"data\":false}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.PlayAllLibrary(false), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryPlayAll);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryplayall");
            response["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        [Fact]
        public void PlayAll_WithStringTrue_PlaysLibraryShuffled()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.PlayAllLibrary(true)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"libraryplayall\",\"data\":\"true\"}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.PlayAllLibrary(true), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryPlayAll);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryplayall");
            response["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        #endregion

        #region Album/Artist/Genre Detail Tests

        [Fact]
        public void AlbumTracks_Query_ReturnsTracksForAlbum()
        {
            // Arrange
            CompleteHandshake();
            var tracks = new List<Track> { new Track { Title = "Track 1", Album = "Test Album", Artist = "Test Artist" } };
            _libraryDataProvider.Setup(x => x.GetAlbumTracks("Test Album", It.IsAny<SearchSource>())).Returns(tracks);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"libraryalbumtracks\",\"data\":\"Test Album\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetAlbumTracks("Test Album", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryAlbumTracks);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryalbumtracks");

            // Returns array of tracks
            var data = response["data"];
            data.Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            data.Should().HaveCount(1);
            data[0]["title"].ToString().Should().Be("Track 1");
            data[0]["album"].ToString().Should().Be("Test Album");
            data[0]["artist"].ToString().Should().Be("Test Artist");
        }

        [Fact]
        public void ArtistAlbums_Query_ReturnsAlbumsForArtist()
        {
            // Arrange
            CompleteHandshake();
            var albums = new List<AlbumData> { new AlbumData("Test Artist", "Album 1") };
            _libraryDataProvider.Setup(x => x.GetArtistAlbums("Test Artist", It.IsAny<SearchSource>())).Returns(albums);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"libraryartistalbums\",\"data\":\"Test Artist\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetArtistAlbums("Test Artist", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryArtistAlbums);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryartistalbums");

            // Returns array of albums
            var data = response["data"];
            data.Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            data.Should().HaveCount(1);
            data[0]["artist"].ToString().Should().Be("Test Artist");
            data[0]["album"].ToString().Should().Be("Album 1");
        }

        [Fact]
        public void GenreArtists_Query_ReturnsArtistsForGenre()
        {
            // Arrange
            CompleteHandshake();
            var artists = new List<ArtistData> { new ArtistData("Rock Artist", 20) };
            _libraryDataProvider.Setup(x => x.GetGenreArtists("Rock", It.IsAny<SearchSource>())).Returns(artists);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"librarygenreartists\",\"data\":\"Rock\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetGenreArtists("Rock", It.IsAny<SearchSource>()), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryGenreArtists);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("librarygenreartists");

            // Returns array of artists
            var data = response["data"];
            data.Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            data.Should().HaveCount(1);
            data[0]["artist"].ToString().Should().Be("Rock Artist");
            data[0]["count"].ToObject<int>().Should().Be(20);
        }

        [Fact]
        public void AlbumTracks_EmptyAlbum_DoesNotProcess()
        {
            // Arrange
            CompleteHandshake();

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"libraryalbumtracks\",\"data\":\"\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetAlbumTracks(It.IsAny<string>(), It.IsAny<SearchSource>()), Times.Never);
        }

        #endregion

        #region Radio Stations Tests

        [Fact]
        public void RadioStations_Query_ReturnsStations()
        {
            // Arrange
            CompleteHandshake();
            var stations = new List<RadioStation> { new RadioStation { Name = "Station 1", Url = "http://test" } };
            _libraryDataProvider.Setup(x => x.GetRadioStations(0, It.IsAny<int>())).Returns(stations);

            // Act
            var request = new { offset = 0, limit = 50 };
            var json = JsonConvert.SerializeObject(new { context = "radiostations", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.GetRadioStations(0, 50), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.RadioStations);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("radiostations");

            // V4 client gets paginated response
            var pageData = response["data"];
            pageData["offset"].ToObject<int>().Should().Be(0);
            pageData["limit"].ToObject<int>().Should().Be(50);
            pageData["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            pageData["data"].Should().HaveCount(1);
            pageData["data"][0]["name"].ToString().Should().Be("Station 1");
            pageData["data"][0]["url"].ToString().Should().Be("http://test");
        }

        #endregion

        #region Cover Cache Tests

        [Fact]
        public void CoverCacheStatus_Query_BroadcastsStatus()
        {
            // Arrange
            CompleteHandshake();
            _coverService.Setup(x => x.BroadcastCacheStatus(ClientId));

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"librarycovercachebuildstatus\",\"data\":\"\"}", ClientId);

            // Assert
            _coverService.Verify(x => x.BroadcastCacheStatus(ClientId), Times.Once);
        }

        #endregion

        #region Album Cover Tests

        [Fact]
        public void AlbumCover_PaginatedRequest_GetsCoverPage()
        {
            // Arrange
            CompleteHandshake();
            var coverPage = new Page<AlbumCoverPayload>
            {
                Total = 100,
                Offset = 0,
                Limit = 50,
                Data = new List<AlbumCoverPayload>
                {
                    new AlbumCoverPayload { Album = "Album1", Artist = "Artist1", Status = 200 }
                }
            };
            _coverService.Setup(x => x.GetCoverPage(0, 50)).Returns(coverPage);

            // Act
            var request = new { offset = 0, limit = 50 };
            var json = JsonConvert.SerializeObject(new { context = "libraryalbumcover", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _coverService.Verify(x => x.GetCoverPage(0, 50), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryAlbumCover);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryalbumcover");

            // Returns paginated response
            var pageData = response["data"];
            pageData["total"].ToObject<int>().Should().Be(100);
            pageData["offset"].ToObject<int>().Should().Be(0);
            pageData["limit"].ToObject<int>().Should().Be(50);
            pageData["data"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Array);
            pageData["data"].Should().HaveCount(1);
            pageData["data"][0]["album"].ToString().Should().Be("Album1");
            pageData["data"][0]["artist"].ToString().Should().Be("Artist1");
            pageData["data"][0]["status"].ToObject<int>().Should().Be(200);
        }

        [Fact]
        public void AlbumCover_ByArtistAlbum_ReturnsSingleCover()
        {
            // Arrange
            CompleteHandshake();
            var coverPayload = new AlbumCoverPayload
            {
                Album = "Abbey Road",
                Artist = "The Beatles",
                Cover = "base64coverdata",
                Status = 200,
                Hash = "abc123"
            };
            _coverService.Setup(x => x.GetAlbumCover("The Beatles", "Abbey Road", "")).Returns(coverPayload);

            // Act - Request cover by artist/album (no hash)
            var request = new { artist = "The Beatles", album = "Abbey Road" };
            var json = JsonConvert.SerializeObject(new { context = "libraryalbumcover", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _coverService.Verify(x => x.GetAlbumCover("The Beatles", "Abbey Road", ""), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryAlbumCover);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("libraryalbumcover");

            // Single cover response (not paginated)
            var data = response["data"];
            data["album"].ToString().Should().Be("Abbey Road");
            data["artist"].ToString().Should().Be("The Beatles");
            data["status"].ToObject<int>().Should().Be(200);
            data["cover"].ToString().Should().Be("base64coverdata");
            data["hash"].ToString().Should().Be("abc123");
        }

        [Fact]
        public void AlbumCover_ByHash_ReturnsCoverIfChanged()
        {
            // Arrange
            CompleteHandshake();
            var coverPayload = new AlbumCoverPayload
            {
                Album = "Abbey Road",
                Artist = "The Beatles",
                Cover = "newbase64cover",
                Status = 200,
                Hash = "newhash456"
            };
            _coverService.Setup(x => x.GetAlbumCover("The Beatles", "Abbey Road", "oldhash123")).Returns(coverPayload);

            // Act - Request cover by artist/album with client's cached hash
            var request = new { artist = "The Beatles", album = "Abbey Road", hash = "oldhash123" };
            var json = JsonConvert.SerializeObject(new { context = "libraryalbumcover", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _coverService.Verify(x => x.GetAlbumCover("The Beatles", "Abbey Road", "oldhash123"), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryAlbumCover);
            response.Should().NotBeNull();
            response["data"]["status"].ToObject<int>().Should().Be(200);
            response["data"]["hash"].ToString().Should().Be("newhash456");
        }

        [Fact]
        public void AlbumCover_BySize_ReturnsSizedCover()
        {
            // Arrange
            CompleteHandshake();
            var coverPayload = new AlbumCoverPayload
            {
                Album = "Test Album",
                Artist = "Test Artist",
                Cover = "resizedbase64",
                Status = 200
            };
            _coverService.Setup(x => x.GetCoverBySize("Test Artist", "Test Album", "300")).Returns(coverPayload);

            // Act - Request cover by size
            var request = new { artist = "Test Artist", album = "Test Album", size = "300" };
            var json = JsonConvert.SerializeObject(new { context = "libraryalbumcover", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _coverService.Verify(x => x.GetCoverBySize("Test Artist", "Test Album", "300"), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryAlbumCover);
            response.Should().NotBeNull();
            response["data"]["status"].ToObject<int>().Should().Be(200);
            response["data"]["cover"].ToString().Should().Be("resizedbase64");
        }

        [Fact]
        public void AlbumCover_NotFound_Returns404Status()
        {
            // Arrange
            CompleteHandshake();
            var coverPayload = new AlbumCoverPayload { Status = 404 };
            _coverService.Setup(x => x.GetAlbumCover("Unknown", "Album", "")).Returns(coverPayload);

            // Act
            var request = new { artist = "Unknown", album = "Album" };
            var json = JsonConvert.SerializeObject(new { context = "libraryalbumcover", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.LibraryAlbumCover);
            response.Should().NotBeNull();
            response["data"]["status"].ToObject<int>().Should().Be(404);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void Command_BeforeHandshake_NotProcessed()
        {
            // Arrange - Don't complete handshake
            _authenticator.AddClientOnConnect(ClientId);
            var disconnectTriggered = false;
            _protocolHandler.ForceClientDisconnect += (id) => disconnectTriggered = true;

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"librarysearchtitle\",\"data\":\"test\"}", ClientId);

            // Assert
            disconnectTriggered.Should().BeTrue();
            _libraryDataProvider.Verify(x => x.SearchTracks(It.IsAny<string>(), It.IsAny<SearchSource>()), Times.Never);
        }

        [Fact]
        public void SearchTitle_EmptyQuery_StillProcesses()
        {
            // Arrange
            CompleteHandshake();
            var tracks = new List<Track>();
            _libraryDataProvider.Setup(x => x.SearchTracks("", It.IsAny<SearchSource>())).Returns(tracks);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"librarysearchtitle\",\"data\":\"\"}", ClientId);

            // Assert
            _libraryDataProvider.Verify(x => x.SearchTracks("", It.IsAny<SearchSource>()), Times.Once);
        }

        [Fact]
        public void QueueTrack_InvalidRequest_DoesNotCrash()
        {
            // Arrange
            CompleteHandshake();

            // Act - Send malformed queue request
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"libraryqueuetrack\",\"data\":\"invalid\"}", ClientId);

            // Assert - Should not throw, handler returns gracefully
            _authenticator.Client(ClientId).Should().NotBeNull();
        }

        #endregion
    }
}
