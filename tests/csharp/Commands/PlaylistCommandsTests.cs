using System.Collections.Generic;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Fixtures;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class PlaylistCommandsTests
    {
        private const string TestConnectionId = "test-connection-123";

        private readonly Mock<IPlaylistDataProvider> _playlistDataProvider;
        private readonly MockLogger _logger;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly Mock<IProtocolCapabilities> _protocolCapabilities;
        private readonly PlaylistCommands _sut;

        public PlaylistCommandsTests()
        {
            _playlistDataProvider = new Mock<IPlaylistDataProvider>();
            _logger = new MockLogger();
            _eventAggregator = new Mock<IEventAggregator>();
            _protocolCapabilities = new Mock<IProtocolCapabilities>();

            _sut = new PlaylistCommands(
                _playlistDataProvider.Object,
                _logger,
                _eventAggregator.Object,
                _protocolCapabilities.Object);
        }

        #region 3.1 Playlist Play

        [Fact]
        public void HandlePlaylistPlay_ValidUrl_PlaysPlaylist()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.PlayPlaylist("playlist://favorites")).Returns(true);
            var context = new TestCommandContext("playlistplay", "playlist://favorites", TestConnectionId);

            // Act
            var result = _sut.HandlePlaylistPlay(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.PlayPlaylist("playlist://favorites"), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlaylistPlay_EmptyUrl_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("playlistplay", string.Empty, TestConnectionId);

            // Act
            var result = _sut.HandlePlaylistPlay(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.PlayPlaylist(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandlePlaylistPlay_NullUrl_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("playlistplay", null, TestConnectionId);

            // Act
            var result = _sut.HandlePlaylistPlay(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.PlayPlaylist(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandlePlaylistPlay_WhitespaceUrl_ReturnsFalse()
        {
            // Arrange
            var context = new TestCommandContext("playlistplay", "   ", TestConnectionId);

            // Act
            var result = _sut.HandlePlaylistPlay(context);

            // Assert
            result.Should().BeFalse();
            _playlistDataProvider.Verify(x => x.PlayPlaylist(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void HandlePlaylistPlay_ProviderFails_StillReturnsTrue()
        {
            // Arrange
            _playlistDataProvider.Setup(x => x.PlayPlaylist("playlist://test")).Returns(false);
            var context = new TestCommandContext("playlistplay", "playlist://test", TestConnectionId);

            // Act
            var result = _sut.HandlePlaylistPlay(context);

            // Assert
            result.Should().BeTrue();
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region 3.2 Playlist List - Legacy Clients

        [Fact]
        public void HandlePlaylistList_LegacyClient_ReturnsNonPagedList()
        {
            // Arrange
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Favorites", Url = "playlist://favorites" },
                new Playlist { Name = "Recently Played", Url = "playlist://recent" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(false);
            var innerContext = new TestCommandContext("playlistlist", null, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.PaginationRequest>(innerContext);

            // Act
            var result = _sut.HandlePlaylistList(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.GetPlaylists(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlaylistList_LegacyClientWithData_ReturnsNonPagedList()
        {
            // Arrange
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Test Playlist", Url = "playlist://test" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(false);
            var data = JObject.FromObject(new { offset = 0, limit = 10 });
            var innerContext = new TestCommandContext("playlistlist", data, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.PaginationRequest>(innerContext);

            // Act
            var result = _sut.HandlePlaylistList(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.GetPlaylists(), Times.Once);
        }

        #endregion

        #region 3.3 Playlist List - Modern Clients

        [Fact]
        public void HandlePlaylistList_ModernClientWithPagination_ReturnsPagedList()
        {
            // Arrange
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Playlist 1", Url = "playlist://1" },
                new Playlist { Name = "Playlist 2", Url = "playlist://2" },
                new Playlist { Name = "Playlist 3", Url = "playlist://3" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(true);
            var paginationData = JObject.FromObject(new { offset = 0, limit = 10 });
            var innerContext = new TestCommandContext("playlistlist", paginationData, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.PaginationRequest>(innerContext);

            // Act
            var result = _sut.HandlePlaylistList(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.GetPlaylists(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlaylistList_ModernClientWithOffset_UsesOffset()
        {
            // Arrange
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Playlist 1", Url = "playlist://1" },
                new Playlist { Name = "Playlist 2", Url = "playlist://2" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(true);
            var paginationData = JObject.FromObject(new { offset = 5, limit = 20 });
            var innerContext = new TestCommandContext("playlistlist", paginationData, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.PaginationRequest>(innerContext);

            // Act
            var result = _sut.HandlePlaylistList(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.GetPlaylists(), Times.Once);
        }

        [Fact]
        public void HandlePlaylistList_ModernClientNoData_UsesLegacyResponse()
        {
            // Arrange
            var playlists = new List<Playlist>
            {
                new Playlist { Name = "Playlist", Url = "playlist://1" }
            };
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(true);
            var innerContext = new TestCommandContext("playlistlist", null, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.PaginationRequest>(innerContext);

            // Act
            var result = _sut.HandlePlaylistList(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.GetPlaylists(), Times.Once);
        }

        #endregion

        #region 3.4 Empty and Edge Cases

        [Fact]
        public void HandlePlaylistList_EmptyPlaylistList_ReturnsEmptyList()
        {
            // Arrange
            var playlists = new List<Playlist>();
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(false);
            var innerContext = new TestCommandContext("playlistlist", null, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.PaginationRequest>(innerContext);

            // Act
            var result = _sut.HandlePlaylistList(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.GetPlaylists(), Times.Once);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void HandlePlaylistList_LargePlaylistCount_HandlesCorrectly()
        {
            // Arrange
            var playlists = new List<Playlist>();
            for (var i = 0; i < 100; i++)
            {
                playlists.Add(new Playlist { Name = $"Playlist {i}", Url = $"playlist://{i}" });
            }
            _playlistDataProvider.Setup(x => x.GetPlaylists()).Returns(playlists);
            _protocolCapabilities.Setup(x => x.SupportsPagination(TestConnectionId)).Returns(true);
            var paginationData = JObject.FromObject(new { offset = 0, limit = 50 });
            var innerContext = new TestCommandContext("playlistlist", paginationData, TestConnectionId);
            var context = new TestTypedCommandContext<MusicBeePlugin.Models.Requests.PaginationRequest>(innerContext);

            // Act
            var result = _sut.HandlePlaylistList(context);

            // Assert
            result.Should().BeTrue();
            _playlistDataProvider.Verify(x => x.GetPlaylists(), Times.Once);
        }

        #endregion
    }
}
