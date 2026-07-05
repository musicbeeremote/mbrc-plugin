using FluentAssertions;
using MusicBeePlugin.Models.Requests;
using Newtonsoft.Json;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Models
{
    public class RequestModelsTests
    {
        #region MoveTrackRequest Tests

        [Fact]
        public void MoveTrackRequest_IsValid_WhenBothFromAndToSet()
        {
            // Arrange
            var request = new MoveTrackRequest { From = 0, To = 5 };

            // Assert
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void MoveTrackRequest_IsNotValid_WhenFromIsNull()
        {
            // Arrange
            var request = new MoveTrackRequest { From = null, To = 5 };

            // Assert
            request.IsValid.Should().BeFalse();
        }

        [Fact]
        public void MoveTrackRequest_IsNotValid_WhenToIsNull()
        {
            // Arrange
            var request = new MoveTrackRequest { From = 0, To = null };

            // Assert
            request.IsValid.Should().BeFalse();
        }

        [Fact]
        public void MoveTrackRequest_IsNotValid_WhenBothNull()
        {
            // Arrange
            var request = new MoveTrackRequest();

            // Assert
            request.IsValid.Should().BeFalse();
        }

        [Fact]
        public void MoveTrackRequest_IsValid_WithNegativeValues()
        {
            // Arrange - negative values are technically valid structurally
            var request = new MoveTrackRequest { From = -1, To = -5 };

            // Assert
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void MoveTrackRequest_IsValid_WhenFromEqualsTo()
        {
            // Arrange
            var request = new MoveTrackRequest { From = 3, To = 3 };

            // Assert
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void MoveTrackRequest_DeserializesFromJson()
        {
            // Arrange
            var json = "{\"from\": 2, \"to\": 5}";

            // Act
            var request = JsonConvert.DeserializeObject<MoveTrackRequest>(json);

            // Assert
            request.From.Should().Be(2);
            request.To.Should().Be(5);
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void MoveTrackRequest_DeserializesWithMissingFrom()
        {
            // Arrange
            var json = "{\"to\": 5}";

            // Act
            var request = JsonConvert.DeserializeObject<MoveTrackRequest>(json);

            // Assert
            request.From.Should().BeNull();
            request.To.Should().Be(5);
            request.IsValid.Should().BeFalse();
        }

        [Fact]
        public void MoveTrackRequest_DeserializesEmptyJson()
        {
            // Arrange
            var json = "{}";

            // Act
            var request = JsonConvert.DeserializeObject<MoveTrackRequest>(json);

            // Assert
            request.From.Should().BeNull();
            request.To.Should().BeNull();
            request.IsValid.Should().BeFalse();
        }

        #endregion

        #region TagChangeRequest Tests

        [Fact]
        public void TagChangeRequest_IsValid_WhenTagIsSet()
        {
            // Arrange
            var request = new TagChangeRequest { Tag = "rating", Value = "5" };

            // Assert
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void TagChangeRequest_IsValid_WithEmptyValue()
        {
            // Arrange - empty value is valid, only tag is required
            var request = new TagChangeRequest { Tag = "rating", Value = string.Empty };

            // Assert
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void TagChangeRequest_IsValid_WithNullValue()
        {
            // Arrange
            var request = new TagChangeRequest { Tag = "rating", Value = null };

            // Assert
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void TagChangeRequest_IsNotValid_WhenTagIsNull()
        {
            // Arrange
            var request = new TagChangeRequest { Tag = null, Value = "5" };

            // Assert
            request.IsValid.Should().BeFalse();
        }

        [Fact]
        public void TagChangeRequest_IsNotValid_WhenTagIsEmpty()
        {
            // Arrange
            var request = new TagChangeRequest { Tag = string.Empty, Value = "5" };

            // Assert
            request.IsValid.Should().BeFalse();
        }

        [Fact]
        public void TagChangeRequest_IsNotValid_WhenTagIsWhitespace()
        {
            // Arrange
            var request = new TagChangeRequest { Tag = "   ", Value = "5" };

            // Assert - whitespace is not considered empty by string.IsNullOrEmpty
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void TagChangeRequest_DeserializesFromJson()
        {
            // Arrange
            var json = "{\"tag\": \"rating\", \"value\": \"5\"}";

            // Act
            var request = JsonConvert.DeserializeObject<TagChangeRequest>(json);

            // Assert
            request.Tag.Should().Be("rating");
            request.Value.Should().Be("5");
            request.IsValid.Should().BeTrue();
        }

        [Fact]
        public void TagChangeRequest_DeserializesWithMissingTag()
        {
            // Arrange
            var json = "{\"value\": \"5\"}";

            // Act
            var request = JsonConvert.DeserializeObject<TagChangeRequest>(json);

            // Assert
            request.Tag.Should().BeNull();
            request.IsValid.Should().BeFalse();
        }

        #endregion

        #region PaginationRequest Tests

        [Fact]
        public void PaginationRequest_DefaultLimit_Is4000()
        {
            // Assert
            PaginationRequest.DefaultLimit.Should().Be(4000);
        }

        [Fact]
        public void PaginationRequest_NewInstance_HasDefaultLimit()
        {
            // Arrange
            var request = new PaginationRequest();

            // Assert
            request.Limit.Should().Be(PaginationRequest.DefaultLimit);
            request.Offset.Should().Be(0);
        }

        [Fact]
        public void PaginationRequest_DeserializesFromJson()
        {
            // Arrange
            var json = "{\"offset\": 10, \"limit\": 50}";

            // Act
            var request = JsonConvert.DeserializeObject<PaginationRequest>(json);

            // Assert
            request.Offset.Should().Be(10);
            request.Limit.Should().Be(50);
        }

        [Fact]
        public void PaginationRequest_DeserializesWithDefaults()
        {
            // Arrange
            var json = "{}";

            // Act
            var request = JsonConvert.DeserializeObject<PaginationRequest>(json);

            // Assert
            request.Offset.Should().Be(0);
            request.Limit.Should().Be(PaginationRequest.DefaultLimit);
        }

        [Fact]
        public void PaginationRequest_DeserializesOnlyOffset()
        {
            // Arrange
            var json = "{\"offset\": 100}";

            // Act
            var request = JsonConvert.DeserializeObject<PaginationRequest>(json);

            // Assert
            request.Offset.Should().Be(100);
            request.Limit.Should().Be(PaginationRequest.DefaultLimit);
        }

        [Fact]
        public void PaginationRequest_DeserializesOnlyLimit()
        {
            // Arrange
            var json = "{\"limit\": 25}";

            // Act
            var request = JsonConvert.DeserializeObject<PaginationRequest>(json);

            // Assert
            request.Offset.Should().Be(0);
            request.Limit.Should().Be(25);
        }

        [Theory]
        [InlineData(0, 50)]
        [InlineData(100, 100)]
        [InlineData(1000, 500)]
        [InlineData(0, 1)]
        public void PaginationRequest_DeserializesVariousValues(int offset, int limit)
        {
            // Arrange
            var json = $"{{\"offset\": {offset}, \"limit\": {limit}}}";

            // Act
            var request = JsonConvert.DeserializeObject<PaginationRequest>(json);

            // Assert
            request.Offset.Should().Be(offset);
            request.Limit.Should().Be(limit);
        }

        #endregion

        #region ProtocolHandshakeRequest Tests

        [Fact]
        public void ProtocolHandshakeRequest_DeserializesFromJson()
        {
            // Arrange
            var json = "{\"protocol_version\": 5, \"no_broadcast\": true, \"client_id\": \"android-001\"}";

            // Act
            var request = JsonConvert.DeserializeObject<ProtocolHandshakeRequest>(json);

            // Assert
            request.ProtocolVersion.Should().Be(5);
            request.NoBroadcast.Should().BeTrue();
            request.ClientId.Should().Be("android-001");
        }

        [Fact]
        public void ProtocolHandshakeRequest_DeserializesWithDefaults()
        {
            // Arrange
            var json = "{}";

            // Act
            var request = JsonConvert.DeserializeObject<ProtocolHandshakeRequest>(json);

            // Assert
            request.ProtocolVersion.Should().Be(0);
            request.NoBroadcast.Should().BeFalse();
            request.ClientId.Should().BeNull();
        }

        [Fact]
        public void ProtocolHandshakeRequest_DeserializesOnlyProtocolVersion()
        {
            // Arrange
            var json = "{\"protocol_version\": 4}";

            // Act
            var request = JsonConvert.DeserializeObject<ProtocolHandshakeRequest>(json);

            // Assert
            request.ProtocolVersion.Should().Be(4);
            request.NoBroadcast.Should().BeFalse();
            request.ClientId.Should().BeNull();
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void ProtocolHandshakeRequest_SupportsMultipleProtocolVersions(int version)
        {
            // Arrange
            var json = $"{{\"protocol_version\": {version}}}";

            // Act
            var request = JsonConvert.DeserializeObject<ProtocolHandshakeRequest>(json);

            // Assert
            request.ProtocolVersion.Should().Be(version);
        }

        [Fact]
        public void ProtocolHandshakeRequest_NoBroadcastFalse()
        {
            // Arrange
            var json = "{\"protocol_version\": 5, \"no_broadcast\": false}";

            // Act
            var request = JsonConvert.DeserializeObject<ProtocolHandshakeRequest>(json);

            // Assert
            request.NoBroadcast.Should().BeFalse();
        }

        #endregion

        #region SearchRequest Tests

        [Fact]
        public void SearchRequest_DeserializesFromJson()
        {
            // Arrange
            var json = "{\"type\": \"artist\", \"query\": \"Beatles\"}";

            // Act
            var request = JsonConvert.DeserializeObject<SearchRequest>(json);

            // Assert
            request.Type.Should().Be("artist");
            request.Query.Should().Be("Beatles");
        }

        [Fact]
        public void SearchRequest_DeserializesWithEmptyQuery()
        {
            // Arrange
            var json = "{\"type\": \"track\", \"query\": \"\"}";

            // Act
            var request = JsonConvert.DeserializeObject<SearchRequest>(json);

            // Assert
            request.Type.Should().Be("track");
            request.Query.Should().BeEmpty();
        }

        [Fact]
        public void SearchRequest_DeserializesWithNullValues()
        {
            // Arrange
            var json = "{}";

            // Act
            var request = JsonConvert.DeserializeObject<SearchRequest>(json);

            // Assert
            request.Type.Should().BeNull();
            request.Query.Should().BeNull();
        }

        [Theory]
        [InlineData("artist")]
        [InlineData("album")]
        [InlineData("track")]
        [InlineData("genre")]
        public void SearchRequest_SupportsMultipleTypes(string type)
        {
            // Arrange
            var json = $"{{\"type\": \"{type}\", \"query\": \"test\"}}";

            // Act
            var request = JsonConvert.DeserializeObject<SearchRequest>(json);

            // Assert
            request.Type.Should().Be(type);
        }

        [Fact]
        public void SearchRequest_HandlesSpecialCharactersInQuery()
        {
            // Arrange
            var json = "{\"type\": \"track\", \"query\": \"Rock & Roll\"}";

            // Act
            var request = JsonConvert.DeserializeObject<SearchRequest>(json);

            // Assert
            request.Query.Should().Be("Rock & Roll");
        }

        #endregion

        #region AlbumCoverRequest Tests

        [Fact]
        public void AlbumCoverRequest_InheritsPaginationProperties()
        {
            // Arrange
            var request = new AlbumCoverRequest();

            // Assert - should have PaginationRequest defaults
            request.Offset.Should().Be(0);
            request.Limit.Should().Be(PaginationRequest.DefaultLimit);
        }

        [Fact]
        public void AlbumCoverRequest_DeserializesFromJson()
        {
            // Arrange
            var json = "{\"album\": \"Abbey Road\", \"artist\": \"Beatles\", \"hash\": \"abc123\", \"size\": \"300\"}";

            // Act
            var request = JsonConvert.DeserializeObject<AlbumCoverRequest>(json);

            // Assert
            request.Album.Should().Be("Abbey Road");
            request.Artist.Should().Be("Beatles");
            request.Hash.Should().Be("abc123");
            request.Size.Should().Be("300");
        }

        [Fact]
        public void AlbumCoverRequest_IsPaginatedRequest_WhenLimitSetAndNoAlbumOrArtist()
        {
            // Arrange
            var request = new AlbumCoverRequest { Limit = 50, Offset = 0 };

            // Assert
            request.IsPaginatedRequest.Should().BeTrue();
        }

        [Fact]
        public void AlbumCoverRequest_IsNotPaginatedRequest_WhenAlbumSet()
        {
            // Arrange
            var request = new AlbumCoverRequest { Limit = 50, Album = "Test Album" };

            // Assert
            request.IsPaginatedRequest.Should().BeFalse();
        }

        [Fact]
        public void AlbumCoverRequest_IsNotPaginatedRequest_WhenArtistSet()
        {
            // Arrange
            var request = new AlbumCoverRequest { Limit = 50, Artist = "Test Artist" };

            // Assert
            request.IsPaginatedRequest.Should().BeFalse();
        }

        [Fact]
        public void AlbumCoverRequest_IsNotPaginatedRequest_WhenLimitIsZero()
        {
            // Arrange
            var request = new AlbumCoverRequest { Limit = 0 };

            // Assert
            request.IsPaginatedRequest.Should().BeFalse();
        }

        [Fact]
        public void AlbumCoverRequest_DeserializesPaginationWithCover()
        {
            // Arrange
            var json = "{\"offset\": 10, \"limit\": 25, \"album\": \"Test\"}";

            // Act
            var request = JsonConvert.DeserializeObject<AlbumCoverRequest>(json);

            // Assert
            request.Offset.Should().Be(10);
            request.Limit.Should().Be(25);
            request.Album.Should().Be("Test");
            request.IsPaginatedRequest.Should().BeFalse();
        }

        #endregion

        #region BrowseArtistsRequest Tests

        [Fact]
        public void BrowseArtistsRequest_InheritsPaginationProperties()
        {
            // Arrange
            var request = new BrowseArtistsRequest();

            // Assert
            request.Offset.Should().Be(0);
            request.Limit.Should().Be(PaginationRequest.DefaultLimit);
        }

        [Fact]
        public void BrowseArtistsRequest_DeserializesFromJson()
        {
            // Arrange
            var json = "{\"album_artists\": true, \"offset\": 0, \"limit\": 50}";

            // Act
            var request = JsonConvert.DeserializeObject<BrowseArtistsRequest>(json);

            // Assert
            request.AlbumArtists.Should().BeTrue();
            request.Offset.Should().Be(0);
            request.Limit.Should().Be(50);
        }

        [Fact]
        public void BrowseArtistsRequest_AlbumArtistsDefaultsToFalse()
        {
            // Arrange
            var json = "{}";

            // Act
            var request = JsonConvert.DeserializeObject<BrowseArtistsRequest>(json);

            // Assert
            request.AlbumArtists.Should().BeFalse();
        }

        [Fact]
        public void BrowseArtistsRequest_DeserializesWithOnlyAlbumArtists()
        {
            // Arrange
            var json = "{\"album_artists\": true}";

            // Act
            var request = JsonConvert.DeserializeObject<BrowseArtistsRequest>(json);

            // Assert
            request.AlbumArtists.Should().BeTrue();
            request.Limit.Should().Be(PaginationRequest.DefaultLimit);
        }

        #endregion

        #region QueueRequest Tests

        [Fact]
        public void QueueRequest_DeserializesFromJson()
        {
            // Arrange
            var json = "{\"queue\": \"next\", \"play\": \"now\", \"data\": [\"track1\", \"track2\"]}";

            // Act
            var request = JsonConvert.DeserializeObject<QueueRequest>(json);

            // Assert
            request.Queue.Should().Be("next");
            request.Play.Should().Be("now");
            request.Data.Should().HaveCount(2);
            request.Data.Should().Contain("track1");
            request.Data.Should().Contain("track2");
        }

        [Fact]
        public void QueueRequest_DeserializesWithEmptyData()
        {
            // Arrange
            var json = "{\"queue\": \"last\", \"data\": []}";

            // Act
            var request = JsonConvert.DeserializeObject<QueueRequest>(json);

            // Assert
            request.Queue.Should().Be("last");
            request.Data.Should().BeEmpty();
        }

        [Fact]
        public void QueueRequest_DeserializesWithNullData()
        {
            // Arrange
            var json = "{\"queue\": \"next\"}";

            // Act
            var request = JsonConvert.DeserializeObject<QueueRequest>(json);

            // Assert
            request.Queue.Should().Be("next");
            request.Data.Should().BeNull();
        }

        [Fact]
        public void QueueRequest_DeserializesWithDefaults()
        {
            // Arrange
            var json = "{}";

            // Act
            var request = JsonConvert.DeserializeObject<QueueRequest>(json);

            // Assert
            request.Queue.Should().BeNull();
            request.Play.Should().BeNull();
            request.Data.Should().BeNull();
        }

        [Theory]
        [InlineData("next")]
        [InlineData("last")]
        [InlineData("now")]
        public void QueueRequest_SupportsMultipleQueueTypes(string queueType)
        {
            // Arrange
            var json = $"{{\"queue\": \"{queueType}\"}}";

            // Act
            var request = JsonConvert.DeserializeObject<QueueRequest>(json);

            // Assert
            request.Queue.Should().Be(queueType);
        }

        #endregion
    }
}
