using System.Collections.Generic;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Configuration;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class CommandRegistryTests
    {
        #region Player Commands Registration

        [Fact]
        public void RegisterPlayerCommands_RegistersAllPlayerCommands()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var playerCommands = CreatePlayerCommands();

            // Act
            CommandRegistry.RegisterPlayerCommands(registrar, playerCommands);

            // Assert
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerPlay);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerPause);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerPlayPause);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerStop);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerNext);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerPrevious);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerVolume);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerMute);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerShuffle);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerScrobble);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerAutoDj);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerRepeat);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerStatus);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerOutput);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlayerOutputSwitch);
        }

        [Fact]
        public void RegisterPlayerCommands_RegistersCorrectCount()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var playerCommands = CreatePlayerCommands();

            // Act
            CommandRegistry.RegisterPlayerCommands(registrar, playerCommands);

            // Assert
            registrar.RegisteredCommands.Should().HaveCount(15);
        }

        #endregion

        #region Now Playing Commands Registration

        [Fact]
        public void RegisterNowPlayingCommands_RegistersAllNowPlayingCommands()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var nowPlayingCommands = CreateNowPlayingCommands();

            // Act
            CommandRegistry.RegisterNowPlayingCommands(registrar, nowPlayingCommands);

            // Assert
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingTrack);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingDetails);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingPosition);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingCover);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingLyrics);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingListMove);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingListRemove);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingListSearch);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingListPlay);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingQueue);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingList);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingTagChange);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingRating);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.NowPlayingLfmRating);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.Init);
        }

        [Fact]
        public void RegisterNowPlayingCommands_RegistersTypedCommands()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var nowPlayingCommands = CreateNowPlayingCommands();

            // Act
            CommandRegistry.RegisterNowPlayingCommands(registrar, nowPlayingCommands);

            // Assert
            registrar.TypedCommands.Should().ContainKey(ProtocolConstants.NowPlayingListMove);
            registrar.TypedCommands[ProtocolConstants.NowPlayingListMove].Should().Be<MoveTrackRequest>();

            registrar.TypedCommands.Should().ContainKey(ProtocolConstants.NowPlayingTagChange);
            registrar.TypedCommands[ProtocolConstants.NowPlayingTagChange].Should().Be<TagChangeRequest>();
        }

        [Fact]
        public void RegisterNowPlayingCommands_RegistersCorrectCount()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var nowPlayingCommands = CreateNowPlayingCommands();

            // Act
            CommandRegistry.RegisterNowPlayingCommands(registrar, nowPlayingCommands);

            // Assert
            registrar.RegisteredCommands.Should().HaveCount(15);
        }

        #endregion

        #region Playlist Commands Registration

        [Fact]
        public void RegisterPlaylistCommands_RegistersAllPlaylistCommands()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var playlistCommands = CreatePlaylistCommands();

            // Act
            CommandRegistry.RegisterPlaylistCommands(registrar, playlistCommands);

            // Assert
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlaylistPlay);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PlaylistList);
        }

        [Fact]
        public void RegisterPlaylistCommands_RegistersTypedPaginationRequest()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var playlistCommands = CreatePlaylistCommands();

            // Act
            CommandRegistry.RegisterPlaylistCommands(registrar, playlistCommands);

            // Assert
            registrar.TypedCommands.Should().ContainKey(ProtocolConstants.PlaylistList);
            registrar.TypedCommands[ProtocolConstants.PlaylistList].Should().Be<PaginationRequest>();
        }

        [Fact]
        public void RegisterPlaylistCommands_RegistersCorrectCount()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var playlistCommands = CreatePlaylistCommands();

            // Act
            CommandRegistry.RegisterPlaylistCommands(registrar, playlistCommands);

            // Assert
            registrar.RegisteredCommands.Should().HaveCount(2);
        }

        #endregion

        #region System Commands Registration

        [Fact]
        public void RegisterSystemCommands_RegistersAllSystemCommands()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var systemCommands = CreateSystemCommands();

            // Act
            CommandRegistry.RegisterSystemCommands(registrar, systemCommands);

            // Assert
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.Protocol);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.Player);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.PluginVersion);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.Ping);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.Pong);
        }

        [Fact]
        public void RegisterSystemCommands_RegistersTypedProtocolHandshakeRequest()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var systemCommands = CreateSystemCommands();

            // Act
            CommandRegistry.RegisterSystemCommands(registrar, systemCommands);

            // Assert
            registrar.TypedCommands.Should().ContainKey(ProtocolConstants.Protocol);
            registrar.TypedCommands[ProtocolConstants.Protocol].Should().Be<ProtocolHandshakeRequest>();
        }

        [Fact]
        public void RegisterSystemCommands_RegistersCorrectCount()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var systemCommands = CreateSystemCommands();

            // Act
            CommandRegistry.RegisterSystemCommands(registrar, systemCommands);

            // Assert
            registrar.RegisteredCommands.Should().HaveCount(5);
        }

        #endregion

        #region Library Commands Registration

        [Fact]
        public void RegisterLibraryCommands_RegistersAllLibraryCommands()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var libraryCommands = CreateLibraryCommands();

            // Act
            CommandRegistry.RegisterLibraryCommands(registrar, libraryCommands);

            // Assert
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.RadioStations);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibrarySearchTitle);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibrarySearchGenre);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibrarySearchArtist);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibrarySearchAlbum);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryQueueTrack);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryQueueGenre);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryQueueArtist);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryQueueAlbum);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryBrowseGenres);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryBrowseArtists);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryBrowseAlbums);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryBrowseTracks);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryPlayAll);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryAlbumTracks);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryArtistAlbums);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryGenreArtists);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryAlbumCover);
            registrar.RegisteredCommands.Should().Contain(ProtocolConstants.LibraryCoverCacheBuildStatus);
        }

        [Fact]
        public void RegisterLibraryCommands_RegistersTypedCommands()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var libraryCommands = CreateLibraryCommands();

            // Act
            CommandRegistry.RegisterLibraryCommands(registrar, libraryCommands);

            // Assert
            registrar.TypedCommands.Should().ContainKey(ProtocolConstants.LibraryQueueTrack);
            registrar.TypedCommands[ProtocolConstants.LibraryQueueTrack].Should().Be<SearchRequest>();

            registrar.TypedCommands.Should().ContainKey(ProtocolConstants.LibraryBrowseArtists);
            registrar.TypedCommands[ProtocolConstants.LibraryBrowseArtists].Should().Be<BrowseArtistsRequest>();

            registrar.TypedCommands.Should().ContainKey(ProtocolConstants.LibraryAlbumCover);
            registrar.TypedCommands[ProtocolConstants.LibraryAlbumCover].Should().Be<AlbumCoverRequest>();
        }

        [Fact]
        public void RegisterLibraryCommands_RegistersCorrectCount()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var libraryCommands = CreateLibraryCommands();

            // Act
            CommandRegistry.RegisterLibraryCommands(registrar, libraryCommands);

            // Assert
            registrar.RegisteredCommands.Should().HaveCount(19);
        }

        #endregion

        #region All Commands Integration

        [Fact]
        public void AllRegistryMethods_RegisterUniqueCommands()
        {
            // Arrange
            var registrar = new TrackingCommandRegistrar();
            var playerCommands = CreatePlayerCommands();
            var nowPlayingCommands = CreateNowPlayingCommands();
            var playlistCommands = CreatePlaylistCommands();
            var systemCommands = CreateSystemCommands();
            var libraryCommands = CreateLibraryCommands();

            // Act
            CommandRegistry.RegisterPlayerCommands(registrar, playerCommands);
            CommandRegistry.RegisterNowPlayingCommands(registrar, nowPlayingCommands);
            CommandRegistry.RegisterPlaylistCommands(registrar, playlistCommands);
            CommandRegistry.RegisterSystemCommands(registrar, systemCommands);
            CommandRegistry.RegisterLibraryCommands(registrar, libraryCommands);

            // Assert - total should be sum of all individual counts (15+15+2+5+19 = 56)
            registrar.RegisteredCommands.Should().HaveCount(56);

            // All commands should be unique
            registrar.RegisteredCommands.Should().OnlyHaveUniqueItems();
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// Mock command registrar that tracks registered commands
        /// </summary>
        private sealed class TrackingCommandRegistrar : ICommandRegistrar
        {
            public List<string> RegisteredCommands { get; } = new List<string>();
            public Dictionary<string, System.Type> TypedCommands { get; } = new Dictionary<string, System.Type>();

            public void RegisterCommand(string command, System.Func<ICommandContext, bool> handler)
            {
                RegisteredCommands.Add(command);
            }

            public void RegisterCommand<TRequest>(string command, System.Func<ITypedCommandContext<TRequest>, bool> handler)
            {
                RegisteredCommands.Add(command);
                TypedCommands[command] = typeof(TRequest);
            }
        }

        private static PlayerCommands CreatePlayerCommands()
        {
            return new PlayerCommands(
                new Mock<IPlayerDataProvider>().Object,
                new MockLogger(),
                new Mock<IEventAggregator>().Object,
                new Mock<IProtocolCapabilities>().Object);
        }

        private static NowPlayingCommands CreateNowPlayingCommands()
        {
            var logger = new MockLogger();
            var broadcaster = new Mock<IBroadcaster>().Object;
            return new NowPlayingCommands(
                new Mock<ITrackDataProvider>().Object,
                new Mock<IPlaylistDataProvider>().Object,
                new Mock<IPlayerDataProvider>().Object,
                logger,
                new Mock<IEventAggregator>().Object,
                new LyricCoverModel(logger, broadcaster),
                broadcaster,
                new Mock<IUserSettings>().Object,
                new Mock<IProtocolCapabilities>().Object);
        }

        private static PlaylistCommands CreatePlaylistCommands()
        {
            return new PlaylistCommands(
                new Mock<IPlaylistDataProvider>().Object,
                new MockLogger(),
                new Mock<IEventAggregator>().Object,
                new Mock<IProtocolCapabilities>().Object);
        }

        private static SystemCommands CreateSystemCommands()
        {
            return new SystemCommands(
                new MockLogger(),
                new Mock<IEventAggregator>().Object,
                new Mock<IAuthenticator>().Object,
                new Mock<IUserSettings>().Object);
        }

        private static LibraryCommands CreateLibraryCommands()
        {
            return new LibraryCommands(
                new Mock<ILibraryDataProvider>().Object,
                new Mock<IPlaylistDataProvider>().Object,
                new Mock<ICoverService>().Object,
                new MockLogger(),
                new Mock<IEventAggregator>().Object,
                new Mock<IUserSettings>().Object);
        }

        #endregion
    }
}
