using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Commands;
using MusicBeePlugin.Models.Configuration;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Protocol.Processing;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    /// Integration tests for now playing commands.
    /// Tests the full message flow: JSON → ProtocolHandler → CommandDispatcher → NowPlayingCommands → Response
    /// </summary>
    public class NowPlayingCommandIntegrationTests
    {
        private const string ClientId = "nowplaying-test-client";

        private readonly Authenticator _authenticator;
        private readonly DelegateCommandDispatcher _dispatcher;
        private readonly MockEventAggregator _eventAggregator;
        private readonly MockLogger _logger;
        private readonly Mock<ITrackDataProvider> _trackDataProvider;
        private readonly Mock<IPlaylistDataProvider> _playlistDataProvider;
        private readonly Mock<IPlayerDataProvider> _playerDataProvider;
        private readonly Mock<IBroadcaster> _broadcaster;
        private readonly Mock<IUserSettings> _userSettings;
        private readonly ProtocolCapabilities _capabilities;
        private readonly NowPlayingCommands _nowPlayingCommands;
        private readonly ProtocolHandler _protocolHandler;
        private readonly LyricCoverModel _lyricCoverModel;

        public NowPlayingCommandIntegrationTests()
        {
            _logger = new MockLogger();
            _authenticator = new Authenticator();
            _eventAggregator = new MockEventAggregator();
            _dispatcher = new DelegateCommandDispatcher(_logger);
            _trackDataProvider = new Mock<ITrackDataProvider>();
            _playlistDataProvider = new Mock<IPlaylistDataProvider>();
            _playerDataProvider = new Mock<IPlayerDataProvider>();
            _broadcaster = new Mock<IBroadcaster>();
            _userSettings = new Mock<IUserSettings>();
            _capabilities = new ProtocolCapabilities(_authenticator);

            // LyricCoverModel needs IPluginLogger and IBroadcaster
            _lyricCoverModel = new LyricCoverModel(_logger, _broadcaster.Object);

            _nowPlayingCommands = new NowPlayingCommands(
                _trackDataProvider.Object,
                _playlistDataProvider.Object,
                _playerDataProvider.Object,
                _logger,
                _eventAggregator,
                _lyricCoverModel,
                _broadcaster.Object,
                _userSettings.Object,
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

            // Register now playing commands
            registrar.RegisterCommand(ProtocolConstants.NowPlayingTrack, _nowPlayingCommands.HandleTrackInfo);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingDetails, _nowPlayingCommands.HandleTrackDetails);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingPosition, _nowPlayingCommands.HandlePlaybackPosition);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingCover, _nowPlayingCommands.HandleCover);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingLyrics, _nowPlayingCommands.HandleLyrics);
            registrar.RegisterCommand<MoveTrackRequest>(ProtocolConstants.NowPlayingListMove, _nowPlayingCommands.HandleMoveTrack);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingListRemove, _nowPlayingCommands.HandleRemoveTrack);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingListSearch, _nowPlayingCommands.HandleSearch);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingListPlay, _nowPlayingCommands.HandleNowPlayingListPlay);
            registrar.RegisterCommand<QueueRequest>(ProtocolConstants.NowPlayingQueue, _nowPlayingCommands.HandleQueue);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.NowPlayingList, _nowPlayingCommands.HandleNowPlayingList);
            registrar.RegisterCommand<TagChangeRequest>(ProtocolConstants.NowPlayingTagChange, _nowPlayingCommands.HandleTagChange);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingRating, _nowPlayingCommands.HandleRating);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingLfmRating, _nowPlayingCommands.HandleLastfmLoveRating);
            registrar.RegisterCommand(ProtocolConstants.Init, _nowPlayingCommands.HandleInit);
        }

        private void CompleteHandshake(int protocolVersion = 4)
        {
            _authenticator.AddClientOnConnect(ClientId);
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"player\",\"data\":\"Android\"}", ClientId);
            _protocolHandler.ProcessIncomingMessage($"{{\"context\":\"protocol\",\"data\":{protocolVersion}}}", ClientId);
            _eventAggregator.Clear();
        }

        #region Track Info Tests

        [Fact]
        public void TrackInfo_Query_ReturnsCurrentTrack()
        {
            // Arrange
            CompleteHandshake();
            var track = new NowPlayingTrackV2
            {
                Artist = "Test Artist",
                Album = "Test Album",
                Title = "Test Title"
            };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(track);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingtrack\",\"data\":\"\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackInfo(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void TrackInfo_Query_ReturnsCorrectJsonResponse()
        {
            // Arrange
            CompleteHandshake();
            var track = new NowPlayingTrackV2
            {
                Artist = "Test Artist",
                Album = "Test Album",
                Title = "Test Song"
            };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(track);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingtrack\",\"data\":\"\"}", ClientId);

            // Assert - Verify serialized response
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.NowPlayingTrack);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be(ProtocolConstants.NowPlayingTrack);
            var data = response["data"];
            data["artist"].ToString().Should().Be("Test Artist");
            data["title"].ToString().Should().Be("Test Song");
            data["album"].ToString().Should().Be("Test Album");
        }

        #endregion

        #region Track Details Tests

        [Fact]
        public void TrackDetails_Query_ReturnsDetails()
        {
            // Arrange
            CompleteHandshake();
            var details = new NowPlayingDetails
            {
                Genre = "Rock",
                Duration = "3:45",
                TrackNo = "5",
                Bitrate = "320"
            };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackDetails()).Returns(details);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingdetails\",\"data\":\"\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackDetails(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void TrackDetails_Query_ReturnsCorrectJsonStructure()
        {
            // Arrange
            CompleteHandshake();
            var details = new NowPlayingDetails
            {
                Genre = "Electronic",
                Duration = "4:30",
                Bitrate = "256"
            };
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackDetails()).Returns(details);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingdetails\",\"data\":\"\"}", ClientId);

            // Assert - Verify serialized response
            var responseData = _eventAggregator.GetFirstResponseData<NowPlayingDetails>(ProtocolConstants.NowPlayingDetails);
            responseData.Should().NotBeNull();
            responseData.Genre.Should().Be("Electronic");
            responseData.Duration.Should().Be("4:30");
            responseData.Bitrate.Should().Be("256");
        }

        #endregion

        #region Playback Position Tests

        [Fact]
        public void PlaybackPosition_Query_ReturnsCurrentPosition()
        {
            // Arrange
            CompleteHandshake();
            var position = new PlaybackPosition(120000, 300000); // 2:00 of 5:00
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(position);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingposition\",\"data\":\"\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.GetPlaybackPosition(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void PlaybackPosition_Query_ReturnsCorrectJsonStructure()
        {
            // Arrange
            CompleteHandshake();
            var position = new PlaybackPosition(60000, 240000); // 1:00 of 4:00
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(position);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingposition\",\"data\":\"\"}", ClientId);

            // Assert - Verify serialized response
            var responseData = _eventAggregator.GetFirstResponseData<PlaybackPosition>(ProtocolConstants.NowPlayingPosition);
            responseData.Should().NotBeNull();
            responseData.Current.Should().Be(60000);
            responseData.Total.Should().Be(240000);
        }

        [Fact]
        public void PlaybackPosition_SetWithInteger_SetsPositionAndBroadcasts()
        {
            // Arrange
            CompleteHandshake();
            var newPosition = new PlaybackPosition(78840, 300000);
            _playerDataProvider.Setup(x => x.SetPosition(78840)).Returns(true);
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(newPosition);

            // Act - Send position as integer
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingposition\",\"data\":78840}", ClientId);

            // Assert - SetPosition should be called
            _playerDataProvider.Verify(x => x.SetPosition(78840), Times.Once);
            _trackDataProvider.Verify(x => x.GetPlaybackPosition(), Times.Once);
        }

        [Fact]
        public void PlaybackPosition_SetWithString_ParsesAndSetsPosition()
        {
            // Arrange
            CompleteHandshake();
            var newPosition = new PlaybackPosition(45000, 180000);
            _playerDataProvider.Setup(x => x.SetPosition(45000)).Returns(true);
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(newPosition);

            // Act - Send position as string
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingposition\",\"data\":\"45000\"}", ClientId);

            // Assert - SetPosition should be called with parsed value
            _playerDataProvider.Verify(x => x.SetPosition(45000), Times.Once);
        }

        [Fact]
        public void PlaybackPosition_SetPosition_BroadcastsToAllClients()
        {
            // Arrange
            CompleteHandshake();
            var newPosition = new PlaybackPosition(30000, 200000);
            _playerDataProvider.Setup(x => x.SetPosition(30000)).Returns(true);
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(newPosition);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingposition\",\"data\":30000}", ClientId);

            // Assert - Message should be broadcast (ConnectionId = "all")
            _eventAggregator.PublishedMessages.Should().ContainSingle();
            var message = _eventAggregator.PublishedMessages.First() as MessageSendEvent;
            message.Should().NotBeNull();
            // Broadcast messages use "all" as ConnectionId
            message.ConnectionId.Should().Be("all");
        }

        [Fact]
        public void PlaybackPosition_Query_RespondsOnlyToRequestingClient()
        {
            // Arrange
            CompleteHandshake();
            var position = new PlaybackPosition(60000, 240000);
            _trackDataProvider.Setup(x => x.GetPlaybackPosition()).Returns(position);

            // Act - Query without position data
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingposition\",\"data\":\"\"}", ClientId);

            // Assert - Should NOT call SetPosition
            _playerDataProvider.Verify(x => x.SetPosition(It.IsAny<int>()), Times.Never);
            // Message should target specific client
            _eventAggregator.PublishedMessages.Should().ContainSingle();
            var message = _eventAggregator.PublishedMessages.First() as MessageSendEvent;
            message.Should().NotBeNull();
            message.ConnectionId.Should().Be(ClientId);
        }

        #endregion

        #region Now Playing List Tests

        [Fact]
        public void NowPlayingList_Query_ReturnsList()
        {
            // Arrange
            CompleteHandshake();
            var tracks = new List<NowPlaying>
            {
                new NowPlaying { Title = "Track 1", Artist = "Artist 1", Position = 1 },
                new NowPlaying { Title = "Track 2", Artist = "Artist 2", Position = 2 }
            };
            _trackDataProvider.Setup(x => x.GetNowPlayingListPage(0, 100)).Returns(tracks);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglist\",\"data\":{\"offset\":0,\"limit\":100}}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingListPage(0, 100), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void NowPlayingList_WithPagination_RespectsOffsetAndLimit()
        {
            // Arrange
            CompleteHandshake();
            var tracks = new List<NowPlaying>
            {
                new NowPlaying { Title = "Track 11", Artist = "Artist 11", Position = 11 }
            };
            _trackDataProvider.Setup(x => x.GetNowPlayingListPage(10, 20)).Returns(tracks);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglist\",\"data\":{\"offset\":10,\"limit\":20}}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingListPage(10, 20), Times.Once);
        }

        #endregion

        #region Now Playing List Play Tests

        [Fact]
        public void NowPlayingListPlay_ValidIndex_PlaysTrack()
        {
            // Arrange
            CompleteHandshake();
            // Android client (from handshake) - index 5 becomes 4 internally (1-based to 0-based)
            _playlistDataProvider.Setup(x => x.PlayNowPlayingByIndex(4)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistplay\",\"data\":5}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.PlayNowPlayingByIndex(4), Times.Once);
        }

        [Fact]
        public void NowPlayingListPlay_StringIndex_ParsesAndPlays()
        {
            // Arrange
            CompleteHandshake();
            // Android index 3 becomes 2 internally
            _playlistDataProvider.Setup(x => x.PlayNowPlayingByIndex(2)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistplay\",\"data\":\"3\"}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.PlayNowPlayingByIndex(2), Times.Once);
        }

        #endregion

        #region Now Playing List Remove Tests

        [Fact]
        public void NowPlayingListRemove_ValidIndex_RemovesTrack()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.RemoveFromNowPlayingList(2)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistremove\",\"data\":2}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.RemoveFromNowPlayingList(2), Times.Once);
        }

        [Fact]
        public void NowPlayingListRemove_ReturnsCorrectResponse()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.RemoveFromNowPlayingList(3)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistremove\",\"data\":3}", ClientId);

            // Assert - Verify serialized response
            var responseData = _eventAggregator.GetFirstResponseData<RemoveTrackResponse>(ProtocolConstants.NowPlayingListRemove);
            responseData.Should().NotBeNull();
            responseData.Success.Should().BeTrue();
            responseData.Index.Should().Be(3);
        }

        #endregion

        #region Now Playing List Move Tests

        [Fact]
        public void NowPlayingListMove_ValidRequest_MovesTrack()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.MoveNowPlayingTrack(1, 5)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistmove\",\"data\":{\"from\":1,\"to\":5}}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.MoveNowPlayingTrack(1, 5), Times.Once);
        }

        [Fact]
        public void NowPlayingListMove_ReturnsCorrectResponse()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.MoveNowPlayingTrack(2, 7)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistmove\",\"data\":{\"from\":2,\"to\":7}}", ClientId);

            // Assert - Verify serialized response
            var responseData = _eventAggregator.GetFirstResponseData<MoveTrackResponse>(ProtocolConstants.NowPlayingListMove);
            responseData.Should().NotBeNull();
            responseData.Success.Should().BeTrue();
            responseData.From.Should().Be(2);
            responseData.To.Should().Be(7);
        }

        #endregion

        #region Queue Tests

        [Fact]
        public void Queue_NextQueue_QueuesTracksNext()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Next, It.IsAny<string[]>(), null)).Returns(true);

            // Act
            var request = new { queue = "next", data = new List<string> { "file1.mp3", "file2.mp3" } };
            var json = JsonConvert.SerializeObject(new { context = "nowplayingqueue", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.QueueFiles(QueueType.Next, It.Is<string[]>(f => f.Length == 2), null), Times.Once);
        }

        [Fact]
        public void Queue_LastQueue_QueuesTracksLast()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Last, It.IsAny<string[]>(), null)).Returns(true);

            // Act
            var request = new { queue = "last", data = new List<string> { "track.mp3" } };
            var json = JsonConvert.SerializeObject(new { context = "nowplayingqueue", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.QueueFiles(QueueType.Last, It.Is<string[]>(f => f.Length == 1), null), Times.Once);
        }

        [Fact]
        public void Queue_PlayNow_PlaysTrackImmediately()
        {
            // Arrange
            CompleteHandshake();
            // Note: The third parameter is request.Play which is null when not provided in request
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.PlayNow, It.IsAny<string[]>(), null)).Returns(true);

            // Act
            var request = new { queue = "now", data = new List<string> { "now.mp3" } };
            var json = JsonConvert.SerializeObject(new { context = "nowplayingqueue", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.QueueFiles(QueueType.PlayNow, It.Is<string[]>(f => f[0] == "now.mp3"), null), Times.Once);
        }

        [Fact]
        public void Queue_ReturnsQueueResponse()
        {
            // Arrange
            CompleteHandshake();
            _playlistDataProvider.Setup(x => x.QueueFiles(QueueType.Next, It.IsAny<string[]>(), null)).Returns(true);

            // Act
            var request = new { queue = "next", data = new List<string> { "test.mp3" } };
            var json = JsonConvert.SerializeObject(new { context = "nowplayingqueue", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert - Verify response
            _eventAggregator.HasResponse(ProtocolConstants.NowPlayingQueue).Should().BeTrue();
            var responseData = _eventAggregator.GetFirstResponseData<QueueResponse>(ProtocolConstants.NowPlayingQueue);
            responseData.Should().NotBeNull();
            responseData.Code.Should().Be(200);
        }

        #endregion

        #region Rating Tests

        [Fact]
        public void Rating_Query_ReturnsCurrentRating()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingrating\",\"data\":\"\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingRating(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Rating_SetValue_UpdatesRating()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.SetNowPlayingRating("5")).Returns(true);
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("5");

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingrating\",\"data\":\"5\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.SetNowPlayingRating("5"), Times.Once);
        }

        [Fact]
        public void Rating_Query_ReturnsCorrectJsonResponse()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("4");

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingrating\",\"data\":\"\"}", ClientId);

            // Assert - Verify serialized response
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.NowPlayingRating);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be(ProtocolConstants.NowPlayingRating);
            response["data"].ToString().Should().Be("4");
        }

        #endregion

        #region LastFM Rating Tests

        [Fact]
        public void LastFmRating_Toggle_TogglesLoveStatus()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Love);
            _trackDataProvider.Setup(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Normal)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglfmrating\",\"data\":\"toggle\"}", ClientId);

            // Assert - Toggle from Love/Ban goes to Normal (toggle from Normal goes to Love)
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Normal), Times.Once);
        }

        [Fact]
        public void LastFmRating_Love_SetsLoved()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Love)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglfmrating\",\"data\":\"love\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Love), Times.Once);
        }

        [Fact]
        public void LastFmRating_Ban_SetsBan()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Ban)).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglfmrating\",\"data\":\"ban\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.SetNowPlayingLastfmStatus(LastfmStatus.Ban), Times.Once);
        }

        #endregion

        #region Lyrics Tests

        [Fact]
        public void Lyrics_Query_ReturnsLyrics()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("Test lyrics content");

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglyrics\",\"data\":\"\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.GetNowPlayingLyrics(), Times.Once);
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Lyrics_Query_ReturnsCorrectJsonStructure()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("Verse 1 lyrics here");

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglyrics\",\"data\":\"\"}", ClientId);

            // Assert - Verify serialized response
            var responseData = _eventAggregator.GetFirstResponseData<LyricsPayload>(ProtocolConstants.NowPlayingLyrics);
            responseData.Should().NotBeNull();
            responseData.Status.Should().Be(200);
            responseData.Lyrics.Should().Be("Verse 1 lyrics here");
        }

        #endregion

        #region Cover Tests

        [Fact]
        public void Cover_Query_ReturnsCover()
        {
            // Arrange
            CompleteHandshake();
            // Cover handler uses LyricCoverModel.Cover, set via SetCover()
            _lyricCoverModel.SetCover("base64coverdata");

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingcover\",\"data\":\"\"}", ClientId);

            // Assert - Cover comes from LyricCoverModel, not ITrackDataProvider
            _eventAggregator.PublishedMessages.Should().ContainSingle();
        }

        [Fact]
        public void Cover_Query_WhenNoCover_Returns404()
        {
            // Arrange
            CompleteHandshake();
            // Don't set any cover - LyricCoverModel.Cover will be null/empty

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingcover\",\"data\":\"\"}", ClientId);

            // Assert - No cover should return status 404
            var responseData = _eventAggregator.GetFirstResponseData<CoverPayload>(ProtocolConstants.NowPlayingCover);
            responseData.Should().NotBeNull();
            responseData.Status.Should().Be(404);
        }

        #endregion

        #region Search Tests

        [Fact]
        public void Search_Query_SearchesNowPlayingList()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.SearchNowPlayingList("test query", It.IsAny<SearchSource>())).Returns("C:\\test.mp3");
            _playlistDataProvider.Setup(x => x.PlayNowPlayingTrack("C:\\test.mp3")).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistsearch\",\"data\":\"test query\"}", ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.SearchNowPlayingList("test query", It.IsAny<SearchSource>()), Times.Once);
        }

        [Fact]
        public void Search_FoundTrack_PlaysTrack()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.SearchNowPlayingList("my song", It.IsAny<SearchSource>())).Returns("C:\\music\\mysong.mp3");
            _playlistDataProvider.Setup(x => x.PlayNowPlayingTrack("C:\\music\\mysong.mp3")).Returns(true);

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistsearch\",\"data\":\"my song\"}", ClientId);

            // Assert
            _playlistDataProvider.Verify(x => x.PlayNowPlayingTrack("C:\\music\\mysong.mp3"), Times.Once);
        }

        #endregion

        #region Init Tests

        [Fact]
        public void Init_Query_SendsInitData()
        {
            // Arrange
            CompleteHandshake();
            var track = new NowPlayingTrackV2 { Artist = "Artist", Title = "Title" };

            _trackDataProvider.Setup(x => x.GetNowPlayingTrackInfo()).Returns(track);
            _trackDataProvider.Setup(x => x.GetNowPlayingRating()).Returns("3");
            _trackDataProvider.Setup(x => x.GetNowPlayingLastfmStatus()).Returns(LastfmStatus.Normal);
            _trackDataProvider.Setup(x => x.GetNowPlayingArtwork()).Returns("coverdata");
            _trackDataProvider.Setup(x => x.GetNowPlayingLyrics()).Returns("lyrics");
            _playerDataProvider.Setup(x => x.GetPlayerStatus(It.IsAny<bool>())).Returns(new PlayerStatus());

            // Act
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"init\",\"data\":\"\"}", ClientId);

            // Assert - Init sends track info, rating, lastfm status, player status, and broadcasts cover/lyrics
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackInfo(), Times.Once);
            _trackDataProvider.Verify(x => x.GetNowPlayingRating(), Times.Once);
            _trackDataProvider.Verify(x => x.GetNowPlayingLastfmStatus(), Times.Once);
            _trackDataProvider.Verify(x => x.GetNowPlayingArtwork(), Times.Once);
            _trackDataProvider.Verify(x => x.GetNowPlayingLyrics(), Times.Once);
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

            // Act - Try to send command before handshake
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayingtrack\",\"data\":\"\"}", ClientId);

            // Assert - Command rejected, disconnect triggered
            disconnectTriggered.Should().BeTrue();
            _trackDataProvider.Verify(x => x.GetNowPlayingTrackInfo(), Times.Never);
        }

        [Fact]
        public void NowPlayingListMove_InvalidRequest_HandlesGracefully()
        {
            // Arrange
            CompleteHandshake();

            // Act - Send invalid move request (missing from/to)
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistmove\",\"data\":{}}", ClientId);

            // Assert - No crash, move not executed
            _playlistDataProvider.Verify(x => x.MoveNowPlayingTrack(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void NowPlayingListRemove_InvalidIndex_HandlesGracefully()
        {
            // Arrange
            CompleteHandshake();

            // Act - Send invalid remove request (negative index)
            _protocolHandler.ProcessIncomingMessage("{\"context\":\"nowplayinglistremove\",\"data\":-1}", ClientId);

            // Assert - No crash, remove not executed
            _playlistDataProvider.Verify(x => x.RemoveFromNowPlayingList(It.IsAny<int>()), Times.Never);
        }

        #endregion

        #region Tag Change Tests

        [Fact]
        public void TagChange_ValidRequest_ChangesTagAndReturnsUpdatedDetails()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns("C:\\Music\\track.mp3");
            _trackDataProvider.Setup(x => x.SetTrackTag("C:\\Music\\track.mp3", "Genre", "Rock")).Returns(true);
            _trackDataProvider.Setup(x => x.CommitTrackTags("C:\\Music\\track.mp3")).Returns(true);
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackDetails()).Returns(new NowPlayingDetails
            {
                Genre = "Rock",
                AlbumArtist = "Test Artist"
            });

            // Act
            var request = new { tag = "Genre", value = "Rock" };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { context = "nowplayingtagchange", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.SetTrackTag("C:\\Music\\track.mp3", "Genre", "Rock"), Times.Once);
            _trackDataProvider.Verify(x => x.CommitTrackTags("C:\\Music\\track.mp3"), Times.Once);

            // Verify response returns updated track details
            var response = _eventAggregator.GetFirstResponseJson(ProtocolConstants.NowPlayingDetails);
            response.Should().NotBeNull();
            response["context"].ToString().Should().Be("nowplayingdetails");
            response["data"]["genre"].ToString().Should().Be("Rock");
        }

        [Fact]
        public void TagChange_MissingTagName_DoesNotProcess()
        {
            // Arrange
            CompleteHandshake();

            // Act - Send request without tag name
            var request = new { value = "Some Value" };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { context = "nowplayingtagchange", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.SetTrackTag(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void TagChange_EmptyTagName_DoesNotProcess()
        {
            // Arrange
            CompleteHandshake();

            // Act
            var request = new { tag = "", value = "Some Value" };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { context = "nowplayingtagchange", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.SetTrackTag(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void TagChange_NoTrackPlaying_DoesNotProcess()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns(string.Empty);

            // Act
            var request = new { tag = "Artist", value = "New Artist" };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { context = "nowplayingtagchange", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert
            _trackDataProvider.Verify(x => x.SetTrackTag(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void TagChange_SetTagFails_DoesNotCommit()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns("C:\\Music\\track.mp3");
            _trackDataProvider.Setup(x => x.SetTrackTag(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(false);

            // Act
            var request = new { tag = "InvalidTag", value = "Value" };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { context = "nowplayingtagchange", data = request });
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert - Should not commit if set fails
            _trackDataProvider.Verify(x => x.CommitTrackTags(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void TagChange_ClearTagValue_SetsEmptyString()
        {
            // Arrange
            CompleteHandshake();
            _trackDataProvider.Setup(x => x.GetNowPlayingFileUrl()).Returns("C:\\Music\\track.mp3");
            _trackDataProvider.Setup(x => x.SetTrackTag("C:\\Music\\track.mp3", "Comment", "")).Returns(true);
            _trackDataProvider.Setup(x => x.CommitTrackTags("C:\\Music\\track.mp3")).Returns(true);
            _trackDataProvider.Setup(x => x.GetNowPlayingTrackDetails()).Returns(new NowPlayingDetails());

            // Act - Send null value to clear tag
            var json = "{\"context\":\"nowplayingtagchange\",\"data\":{\"tag\":\"Comment\",\"value\":null}}";
            _protocolHandler.ProcessIncomingMessage(json, ClientId);

            // Assert - Should set empty string when value is null
            _trackDataProvider.Verify(x => x.SetTrackTag("C:\\Music\\track.mp3", "Comment", ""), Times.Once);
        }

        #endregion
    }
}
