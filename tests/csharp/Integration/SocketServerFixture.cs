using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Protocol.Processing;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    ///     Test fixture that sets up a real SocketServer with mocked data providers.
    ///     Use IAsyncLifetime for proper async setup/teardown.
    /// </summary>
    public class SocketServerFixture : IAsyncLifetime
    {
        private readonly List<ArtistData> _testArtists;
        private readonly List<AlbumData> _testAlbums;
        private readonly List<Track> _testTracks;
        private readonly List<GenreData> _testGenres;

        public SocketServerFixture()
        {
            // Generate test data - 300 items each (50 genres for variety)
            _testArtists = GenerateArtists(300);
            _testAlbums = GenerateAlbums(300);
            _testTracks = GenerateTracks(300);
            _testGenres = GenerateGenres(50);

            Port = GetAvailablePort();
            Logger = new MockLogger();

            SetupMocks();
            SetupServer();
        }

        public int Port { get; }
        public MockLogger Logger { get; }
        public SocketServer Server { get; private set; }

        public Mock<ILibraryDataProvider> LibraryDataProvider { get; private set; }
        public Mock<IPlaylistDataProvider> PlaylistDataProvider { get; private set; }
        public Mock<ICoverService> CoverService { get; private set; }
        public Mock<IUserSettings> UserSettings { get; private set; }
        public Mock<IEventAggregator> EventAggregator { get; private set; }

        public IReadOnlyList<ArtistData> TestArtists => _testArtists;
        public IReadOnlyList<AlbumData> TestAlbums => _testAlbums;
        public IReadOnlyList<Track> TestTracks => _testTracks;
        public IReadOnlyList<GenreData> TestGenres => _testGenres;

        public Task InitializeAsync()
        {
            Server.StartListening();

            // Give the server a moment to start
            return Task.Delay(100);
        }

        public Task DisposeAsync()
        {
            Server?.Dispose();
            return Task.CompletedTask;
        }

        private void SetupMocks()
        {
            EventAggregator = new Mock<IEventAggregator>();

            // Set up the generic Publish<T> method for MessageSendEvent
            EventAggregator.Setup(x => x.Publish(It.IsAny<MessageSendEvent>()))
                .Callback<MessageSendEvent>(msg =>
                {
                    // Route MessageSendEvent to the socket server
                    if (Server != null)
                    {
                        Server.Send(msg.Message, msg.ConnectionId);
                    }
                });

            // Set up the generic PublishAsync<T> method for MessageSendEvent
            EventAggregator.Setup(x => x.PublishAsync(It.IsAny<MessageSendEvent>()))
                .Callback<MessageSendEvent>(msg =>
                {
                    // Route MessageSendEvent to the socket server
                    if (Server != null)
                    {
                        Server.Send(msg.Message, msg.ConnectionId);
                    }
                })
                .Returns(Task.CompletedTask);

            // Set up the generic PublishAsync<T> method for SocketStatusChangeEvent (used by SocketServer)
            EventAggregator.Setup(x => x.PublishAsync(It.IsAny<SocketStatusChangeEvent>()))
                .Returns(Task.CompletedTask);

            UserSettings = new Mock<IUserSettings>();
            UserSettings.Setup(x => x.ListeningPort).Returns((uint)Port);
            UserSettings.Setup(x => x.FilterSelection).Returns(FilteringSelection.All);
            UserSettings.Setup(x => x.CurrentVersion).Returns("1.0.0-test");
            UserSettings.Setup(x => x.Source).Returns(SearchSource.Library);

            LibraryDataProvider = new Mock<ILibraryDataProvider>();
            // Note: PagedResponseHelper.CreatePagedMessage expects ALL data and does pagination internally
            // BrowseArtists returns IEnumerable<ArtistData>
            LibraryDataProvider.Setup(x => x.BrowseArtists(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
                .Returns((int offset, int limit, bool albumArtists) => _testArtists);
            // BrowseAlbums returns List<AlbumData>
            LibraryDataProvider.Setup(x => x.BrowseAlbums(It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int offset, int limit) => _testAlbums);
            // BrowseTracks returns IEnumerable<Track>
            LibraryDataProvider.Setup(x => x.BrowseTracks(It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int offset, int limit) => _testTracks);
            // BrowseGenres returns IEnumerable<GenreData>
            LibraryDataProvider.Setup(x => x.BrowseGenres(It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int offset, int limit) => _testGenres);

            PlaylistDataProvider = new Mock<IPlaylistDataProvider>();
            PlaylistDataProvider.Setup(x => x.GetPlaylists()).Returns(new List<Playlist>());

            CoverService = new Mock<ICoverService>();
        }

        private void SetupServer()
        {
            var authenticator = new Authenticator();
            var protocolCapabilities = new ProtocolCapabilities(authenticator);

            // Create command dispatcher
            var commandDispatcher = new DelegateCommandDispatcher(Logger);

            // Create command handlers
            var libraryCommands = new LibraryCommands(
                LibraryDataProvider.Object,
                PlaylistDataProvider.Object,
                CoverService.Object,
                Logger,
                EventAggregator.Object,
                UserSettings.Object);

            var playlistCommands = new PlaylistCommands(
                PlaylistDataProvider.Object,
                Logger,
                EventAggregator.Object,
                protocolCapabilities);

            var systemCommands = new SystemCommands(
                Logger,
                EventAggregator.Object,
                authenticator,
                UserSettings.Object);

            // Register commands using ICommandRegistrar interface
            ICommandRegistrar registrar = commandDispatcher;
            CommandRegistry.RegisterSystemCommands(registrar, systemCommands);
            CommandRegistry.RegisterLibraryCommands(registrar, libraryCommands);
            CommandRegistry.RegisterPlaylistCommands(registrar, playlistCommands);

            var protocolHandler = new ProtocolHandler(
                Logger,
                authenticator,
                EventAggregator.Object,
                commandDispatcher);

            Server = new SocketServer(
                protocolHandler,
                authenticator,
                UserSettings.Object,
                EventAggregator.Object,
                Logger);
        }

        private static int GetAvailablePort()
        {
            // Find an available port by binding to port 0
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static List<ArtistData> GenerateArtists(int count)
        {
            var artists = new List<ArtistData>(count);
            for (var i = 1; i <= count; i++)
            {
                artists.Add(new ArtistData($"Test Artist {i:D3}", 10));
            }
            return artists;
        }

        private static List<AlbumData> GenerateAlbums(int count)
        {
            var albums = new List<AlbumData>(count);
            for (var i = 1; i <= count; i++)
            {
                albums.Add(new AlbumData(
                    $"Test Artist {(i % 50) + 1:D3}",
                    $"Test Album {i:D3}"));
            }
            return albums;
        }

        private static List<Track> GenerateTracks(int count)
        {
            var tracks = new List<Track>(count);
            for (var i = 1; i <= count; i++)
            {
                tracks.Add(new Track
                {
                    Title = $"Test Track {i:D3}",
                    Artist = $"Test Artist {(i % 50) + 1:D3}",
                    Album = $"Test Album {(i % 100) + 1:D3}",
                    Src = $"file:///music/track_{i:D3}.mp3"
                });
            }
            return tracks;
        }

        private static List<GenreData> GenerateGenres(int count)
        {
            var genres = new List<GenreData>(count);
            for (var i = 1; i <= count; i++)
            {
                genres.Add(new GenreData($"Test Genre {i:D2}", 20));
            }
            return genres;
        }
    }
}
