using FluentAssertions;
using Moq;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Core;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Services
{
    public class BroadcasterTests
    {
        private readonly Mock<IEventAggregator> _eventAggregator;
        private readonly Broadcaster _sut;

        public BroadcasterTests()
        {
            _eventAggregator = new Mock<IEventAggregator>();
            _sut = new Broadcaster(_eventAggregator.Object);
        }

        [Fact]
        public void BroadcastCover_PublishesBroadcastEvent()
        {
            // Act
            _sut.BroadcastCover("cover-hash-123");

            // Assert
            _eventAggregator.Verify(x => x.Publish(It.IsAny<BroadcastEvent>()), Times.Once);
        }

        [Fact]
        public void BroadcastCover_WithValidCover_CreatesCorrectEvent()
        {
            // Arrange
            BroadcastEvent capturedEvent = null;
            _eventAggregator.Setup(x => x.Publish(It.IsAny<BroadcastEvent>()))
                .Callback<BroadcastEvent>(e => capturedEvent = e);

            // Act
            _sut.BroadcastCover("cover-hash-123");

            // Assert
            capturedEvent.Should().NotBeNull();
            var v2Message = capturedEvent.GetMessage(2);
            v2Message.Should().Contain("nowplayingcover");
        }

        [Fact]
        public void BroadcastCover_WithEmptyCover_StillPublishesEvent()
        {
            // Act
            _sut.BroadcastCover(string.Empty);

            // Assert
            _eventAggregator.Verify(x => x.Publish(It.IsAny<BroadcastEvent>()), Times.Once);
        }

        [Fact]
        public void BroadcastCover_WithNullCover_StillPublishesEvent()
        {
            // Act
            _sut.BroadcastCover(null);

            // Assert
            _eventAggregator.Verify(x => x.Publish(It.IsAny<BroadcastEvent>()), Times.Once);
        }

        [Fact]
        public void BroadcastLyrics_PublishesBroadcastEvent()
        {
            // Act
            _sut.BroadcastLyrics("Some lyrics here");

            // Assert
            _eventAggregator.Verify(x => x.Publish(It.IsAny<BroadcastEvent>()), Times.Once);
        }

        [Fact]
        public void BroadcastLyrics_WithValidLyrics_CreatesCorrectEvent()
        {
            // Arrange
            BroadcastEvent capturedEvent = null;
            _eventAggregator.Setup(x => x.Publish(It.IsAny<BroadcastEvent>()))
                .Callback<BroadcastEvent>(e => capturedEvent = e);

            // Act
            _sut.BroadcastLyrics("Test lyrics content");

            // Assert
            capturedEvent.Should().NotBeNull();
            var v2Message = capturedEvent.GetMessage(2);
            v2Message.Should().Contain("nowplayinglyrics");
        }

        [Fact]
        public void BroadcastLyrics_WithEmptyLyrics_UsesNotFoundMessage()
        {
            // Arrange
            BroadcastEvent capturedEvent = null;
            _eventAggregator.Setup(x => x.Publish(It.IsAny<BroadcastEvent>()))
                .Callback<BroadcastEvent>(e => capturedEvent = e);

            // Act
            _sut.BroadcastLyrics(string.Empty);

            // Assert
            capturedEvent.Should().NotBeNull();
            var v2Message = capturedEvent.GetMessage(2);
            v2Message.Should().Contain("Lyrics Not Found");
        }

        [Fact]
        public void BroadcastLyrics_WithNullLyrics_UsesNotFoundMessage()
        {
            // Arrange
            BroadcastEvent capturedEvent = null;
            _eventAggregator.Setup(x => x.Publish(It.IsAny<BroadcastEvent>()))
                .Callback<BroadcastEvent>(e => capturedEvent = e);

            // Act
            _sut.BroadcastLyrics(null);

            // Assert
            capturedEvent.Should().NotBeNull();
            var v2Message = capturedEvent.GetMessage(2);
            v2Message.Should().Contain("Lyrics Not Found");
        }

        [Fact]
        public void BroadcastCover_IncludesV2AndV3Payloads()
        {
            // Arrange
            BroadcastEvent capturedEvent = null;
            _eventAggregator.Setup(x => x.Publish(It.IsAny<BroadcastEvent>()))
                .Callback<BroadcastEvent>(e => capturedEvent = e);

            // Act
            _sut.BroadcastCover("test-cover");

            // Assert
            capturedEvent.Should().NotBeNull();
            // V2 message should exist
            capturedEvent.GetMessage(2).Should().NotBeEmpty();
            // V3 message should exist
            capturedEvent.GetMessage(3).Should().NotBeEmpty();
        }

        [Fact]
        public void BroadcastLyrics_IncludesV2AndV3Payloads()
        {
            // Arrange
            BroadcastEvent capturedEvent = null;
            _eventAggregator.Setup(x => x.Publish(It.IsAny<BroadcastEvent>()))
                .Callback<BroadcastEvent>(e => capturedEvent = e);

            // Act
            _sut.BroadcastLyrics("test lyrics");

            // Assert
            capturedEvent.Should().NotBeNull();
            // V2 message should exist
            capturedEvent.GetMessage(2).Should().NotBeEmpty();
            // V3 message should exist
            capturedEvent.GetMessage(3).Should().NotBeEmpty();
        }
    }
}
