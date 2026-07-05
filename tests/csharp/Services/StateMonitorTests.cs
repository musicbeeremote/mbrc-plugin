using System;
using FluentAssertions;
using Moq;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Services.Core;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Services
{
    public class StateMonitorTests : IDisposable
    {
        private readonly Mock<IPlayerDataProvider> _playerDataProvider;
        private readonly Mock<ITrackDataProvider> _trackDataProvider;
        private readonly Mock<IEventAggregator> _eventAggregator;
        private StateMonitor _sut;

        public StateMonitorTests()
        {
            _playerDataProvider = new Mock<IPlayerDataProvider>();
            _trackDataProvider = new Mock<ITrackDataProvider>();
            _eventAggregator = new Mock<IEventAggregator>();

            // Setup default return values
            _playerDataProvider.Setup(x => x.GetShuffleState()).Returns(ShuffleState.Off);
            _playerDataProvider.Setup(x => x.GetScrobbleEnabled()).Returns(false);
            _playerDataProvider.Setup(x => x.GetRepeatMode()).Returns(RepeatMode.None);
            _playerDataProvider.Setup(x => x.GetPlayState()).Returns(PlayState.Stopped);
            _trackDataProvider.Setup(x => x.GetPlaybackPosition())
                .Returns(new PlaybackPosition(0, 0));

            _sut = new StateMonitor(_playerDataProvider.Object, _trackDataProvider.Object, _eventAggregator.Object);
        }

        public void Dispose()
        {
            _sut?.Dispose();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Constructor_DoesNotStartMonitoring()
        {
            // Assert - no timers should be running yet
            // Give a small delay to see if any events fire
            System.Threading.Thread.Sleep(50);
            _eventAggregator.Verify(x => x.Publish(It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public void StartMonitoring_InitializesState()
        {
            // Act
            _sut.StartMonitoring();

            // Assert - should query initial state
            _playerDataProvider.Verify(x => x.GetShuffleState(), Times.Once);
            _playerDataProvider.Verify(x => x.GetScrobbleEnabled(), Times.Once);
            _playerDataProvider.Verify(x => x.GetRepeatMode(), Times.Once);
        }

        [Fact]
        public void StopMonitoring_CanBeCalledWithoutStarting()
        {
            // Act
            Action act = () => _sut.StopMonitoring();

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void StopMonitoring_CanBeCalledMultipleTimes()
        {
            // Arrange
            _sut.StartMonitoring();

            // Act
            Action act = () =>
            {
                _sut.StopMonitoring();
                _sut.StopMonitoring();
                _sut.StopMonitoring();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_StopsMonitoring()
        {
            // Arrange
            _sut.StartMonitoring();

            // Act
            _sut.Dispose();

            // Assert - disposing should not throw
            // Further calls should be safe
            Action act = () => _sut.Dispose();
            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Act
            Action act = () =>
            {
                _sut.Dispose();
                _sut.Dispose();
                _sut.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void StartMonitoring_ThenStop_DoesNotThrow()
        {
            // Act
            Action act = () =>
            {
                _sut.StartMonitoring();
                _sut.StopMonitoring();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void StartMonitoring_CanBeCalledAfterStop()
        {
            // Act
            Action act = () =>
            {
                _sut.StartMonitoring();
                _sut.StopMonitoring();
                _sut.StartMonitoring();
                _sut.StopMonitoring();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Constructor_AcceptsValidDependencies()
        {
            // Act
            Action act = () => new StateMonitor(
                _playerDataProvider.Object,
                _trackDataProvider.Object,
                _eventAggregator.Object);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void StartMonitoring_QueriesInitialShuffleState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetShuffleState()).Returns(ShuffleState.Shuffle);

            // Act
            _sut.StartMonitoring();

            // Assert
            _playerDataProvider.Verify(x => x.GetShuffleState(), Times.Once);
        }

        [Fact]
        public void StartMonitoring_QueriesInitialRepeatMode()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetRepeatMode()).Returns(RepeatMode.All);

            // Act
            _sut.StartMonitoring();

            // Assert
            _playerDataProvider.Verify(x => x.GetRepeatMode(), Times.Once);
        }

        [Fact]
        public void StartMonitoring_QueriesInitialScrobbleState()
        {
            // Arrange
            _playerDataProvider.Setup(x => x.GetScrobbleEnabled()).Returns(true);

            // Act
            _sut.StartMonitoring();

            // Assert
            _playerDataProvider.Verify(x => x.GetScrobbleEnabled(), Times.Once);
        }
    }
}
