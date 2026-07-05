using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Models.Responses;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Protocol.Processing;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    /// Integration tests for playlist commands.
    /// Tests the full message flow: JSON → ProtocolHandler → CommandDispatcher → PlaylistCommands → Response
    /// </summary>
    public class PlaylistCommandIntegrationTests
    {
        private const string ClientId = "playlist-test-client";

        private readonly Authenticator _authenticator;
        private readonly DelegateCommandDispatcher _dispatcher;
        private readonly MockEventAggregator _eventAggregator;
        private readonly MockLogger _logger;
        private readonly Mock<IPlaylistDataProvider> _playlistDataProvider;
        private readonly ProtocolCapabilities _capabilities;
        private readonly PlaylistCommands _playlistCommands;
        private readonly ProtocolHandler _protocolHandler;

        public PlaylistCommandIntegrationTests()
        {
            _logger = new MockLogger();
            _authenticator = new Authenticator();
            _eventAggregator = new MockEventAggregator();
            _dispatcher = new DelegateCommandDispatcher(_logger);
            _playlistDataProvider = new Mock<IPlaylistDataProvider>();
            _capabilities = new ProtocolCapabilities(_authenticator);

            _playlistCommands = new PlaylistCommands(
                _playlistDataProvider.Object,
                _logger,
                _eventAggregator,
                _capabilities);

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

            // Register playlist commands
            registrar.RegisterCommand(ProtocolConstants.PlaylistPlay, _playlistCommands.HandlePlaylistPlay);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.PlaylistList, _playlistCommands.HandlePlaylistList);
        }

        private void CompleteHandshake(int protocolVersion = 4)
        {
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage($"{{\"context\":\"protocol\",\"data\":{protocolVersion}}}", ClientId);
            _eventAggregator.Clear();
        }

        #region PlaylistPlay Tests

        [Fact]
        public void PlaylistPlay_ValidUrl_PlaysPlaylist()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.PlayPlaylist("C:\\Music\\MyPlaylist.m3u")).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playlistplay\",\"data\":\"C:\\\\Music\\\\MyPlaylist.m3u\"}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.PlayPlaylist("C:\\Music\\MyPlaylist.m3u"), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistPlay);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playlistplay");
            response["data"].Type.Should().Be(JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        [Fact]
        public void PlaylistPlay_ValidUrl_ReturnsSuccessResponse()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.PlayPlaylist(It.IsAny<string>())).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playlistplay\",\"data\":\"playlist.m3u\"}", ClientId);

            // Assert
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistPlay);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playlistplay");
            response["data"].Type.Should().Be(JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeTrue();
        }

        [Fact]
        public void PlaylistPlay_FailedPlay_ReturnsFalseResponse()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.PlayPlaylist(It.IsAny<string>())).Returns(false);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playlistplay\",\"data\":\"invalid.m3u\"}", ClientId);

            // Assert
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistPlay);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playlistplay");
            response["data"].Type.Should().Be(JTokenType.Boolean);
            response["data"].ToObject<bool>().Should().BeFalse();
        }

        [Fact]
        public void PlaylistPlay_EmptyUrl_DoesNotPlay()
        {
            // Arrange
            CompleteHandshake();

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playlistplay\",\"data\":\"\"}", ClientId);

            // Assert - Empty URL should not trigger PlayPlaylist
            _playlistDataProvider.Verify(x => x.PlayPlaylist(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void PlaylistPlay_NullData_DoesNotPlay()
        {
            // Arrange
            CompleteHandshake();

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playlistplay\",\"data\":null}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.PlayPlaylist(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region PlaylistList Tests

        [Fact]
        public void PlaylistList_Query_ReturnsPlaylists()
        {
            // Arrange
            CompleteHandshake();
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Favorites", Url = "C:\\Music\\Favorites.m3u" },
                new Playlist { Name = "Rock", Url = "C:\\Music\\Rock.m3u" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);

            // Act
            var request = new { offset = 0, limit = 100 };
            var json = JsonConvert.SerializeObject(new { context = "playlistlist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.GetPlaylists(), Times.Once);
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistList);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playlistlist");

            // V4 client gets paginated response
            var pageData = response["data"];
            pageData["offset"].ToObject<int>().Should().Be(0);
            pageData["limit"].ToObject<int>().Should().Be(100);
            pageData["data"].Type.Should().Be(JTokenType.Array);
            pageData["data"].Should().HaveCount(2);
            pageData["data"][0]["name"].ToString().Should().Be("Favorites");
            pageData["data"][0]["url"].ToString().Should().Be("C:\\Music\\Favorites.m3u");
        }

        [Fact]
        public void PlaylistList_V4Client_ReturnsPaginatedResponse()
        {
            // Arrange
            CompleteHandshake(protocolVersion: 4); // V4 supports pagination
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Playlist 1", Url = "url1" },
                new Playlist { Name = "Playlist 2", Url = "url2" },
                new Playlist { Name = "Playlist 3", Url = "url3" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);

            // Act
            var request = new { offset = 0, limit = 10 };
            var json = JsonConvert.SerializeObject(new { context = "playlistlist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert - V4 clients get paginated response with total, offset, limit, data
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistList);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playlistlist");

            var pageData = response["data"];
            pageData["total"].ToObject<int>().Should().Be(3);
            pageData["offset"].ToObject<int>().Should().Be(0);
            pageData["limit"].ToObject<int>().Should().Be(10);
            pageData["data"].Type.Should().Be(JTokenType.Array);
            pageData["data"].Should().HaveCount(3);
            pageData["data"][0]["name"].ToString().Should().Be("Playlist 1");
            pageData["data"][1]["name"].ToString().Should().Be("Playlist 2");
            pageData["data"][2]["name"].ToString().Should().Be("Playlist 3");
        }

        [Fact]
        public void PlaylistList_V2Client_ReturnsNonPaginatedResponse()
        {
            // Arrange
            CompleteHandshake(protocolVersion: 2); // V2 doesn't support pagination
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "My Playlist", Url = "myplaylist.m3u" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);

            // Act
            var request = new { offset = 0, limit = 100 };
            var json = JsonConvert.SerializeObject(new { context = "playlistlist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert - V2 clients get raw list, not paginated
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistList);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playlistlist");

            // For non-paginated, data should be an array directly
            var data = response["data"];
            data.Type.Should().Be(JTokenType.Array);
            data.Should().HaveCount(1);
            data[0]["name"].ToString().Should().Be("My Playlist");
            data[0]["url"].ToString().Should().Be("myplaylist.m3u");
        }

        [Fact]
        public void PlaylistList_EmptyList_ReturnsEmptyResponse()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(new List<Playlist>());

            // Act
            var request = new { offset = 0, limit = 100 };
            var json = JsonConvert.SerializeObject(new { context = "playlistlist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistList);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playlistlist");

            // V4 returns paginated response with empty data array
            var pageData = response["data"];
            pageData["total"].ToObject<int>().Should().Be(0);
            pageData["data"].Type.Should().Be(JTokenType.Array);
            pageData["data"].Should().BeEmpty();
        }

        [Fact]
        public void PlaylistList_WithOffset_ReturnsCorrectPage()
        {
            // Arrange
            CompleteHandshake(protocolVersion: 4);
            var playlists = Enumerable.Range(1, 50)
                .Select(i => new Playlist { Name = $"Playlist {i}", Url = $"url{i}" })
                .ToList();
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);

            // Act
            var request = new { offset = 10, limit = 5 };
            var json = JsonConvert.SerializeObject(new { context = "playlistlist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistList);
            var data = response["data"];
            data["total"].ToObject<long>().Should().Be(50);
            data["offset"].ToObject<long>().Should().Be(10);
            data["limit"].ToObject<long>().Should().Be(5);
            data["data"].ToObject<List<Playlist>>().Should().HaveCount(5);
        }

        [Fact]
        public void PlaylistList_OffsetBeyondTotal_ReturnsEmptyData()
        {
            // Arrange
            CompleteHandshake(protocolVersion: 4);
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Only One", Url = "one.m3u" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);

            // Act
            var request = new { offset = 100, limit = 10 };
            var json = JsonConvert.SerializeObject(new { context = "playlistlist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistList);
            var data = response["data"];
            data["total"].ToObject<long>().Should().Be(1);
            data["data"].ToObject<List<Playlist>>().Should().BeEmpty();
        }

        [Fact]
        public void PlaylistList_DefaultLimit_UsesDefaultValue()
        {
            // Arrange
            CompleteHandshake(protocolVersion: 4);
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Test", Url = "test.m3u" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);

            // Act - Send request without limit (should use default 4000)
            var request = new { offset = 0 };
            var json = JsonConvert.SerializeObject(new { context = "playlistlist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistList);
            response.Should().NotBeNull();
        }

        #endregion

        #region Response Verification Tests

        [Fact]
        public void PlaylistPlay_ReturnsCorrectJsonStructure()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.PlayPlaylist(It.IsAny<string>())).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playlistplay\",\"data\":\"test.m3u\"}", ClientId);

            // Assert
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistPlay);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("playlistplay");
            response["data"].Type.Should().Be(JTokenType.Boolean);
        }

        [Fact]
        public void PlaylistList_ReturnsCorrectPaginatedStructure()
        {
            // Arrange
            CompleteHandshake(protocolVersion: 4);
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Favorites", Url = "favorites.m3u" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);

            // Act
            var request = new { offset = 0, limit = 50 };
            var json = JsonConvert.SerializeObject(new { context = "playlistlist", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert - Verify full JSON structure
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.PlaylistList);
            response["context"].ToString().Should().Be("playlistlist");

            var pageData = response["data"];
            pageData["total"].ToObject<long>().Should().Be(1);
            pageData["offset"].ToObject<long>().Should().Be(0);
            pageData["limit"].ToObject<long>().Should().Be(50);

            var items = pageData["data"].ToObject<List<Playlist>>();
            items.Should().HaveCount(1);
            items[0].Name.Should().Be("Favorites");
            items[0].Url.Should().Be("favorites.m3u");
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
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playlistplay\",\"data\":\"test.m3u\"}", ClientId);

            // Assert
            disconnectTriggered.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.PlayPlaylist(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void PlaylistList_InvalidRequest_DoesNotCrash()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(new List<Playlist>());

            // Act - Send malformed request
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"playlistlist\",\"data\":\"invalid\"}", ClientId);

            // Assert - Should not throw, handler returns gracefully
            _authenticator.Client(ClientId).Should().NotBeNull();
        }

        #endregion
    }
}
