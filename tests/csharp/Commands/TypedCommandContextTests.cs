using FluentAssertions;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Models.Requests;
using MusicBeeRemote.Core.Tests.Fixtures;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class TypedCommandContextTests
    {
        private const string TestConnectionId = "test-connection-123";

        #region Constructor and Basic Properties

        [Fact]
        public void Constructor_PreservesInnerContextProperties()
        {
            // Arrange
            var innerContext = new TestCommandContext("test-command", "data", TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<string>(innerContext);

            // Assert
            typedContext.CommandType.Should().Be("test-command");
            typedContext.ConnectionId.Should().Be(TestConnectionId);
            typedContext.Data.Should().Be("data");
        }

        [Fact]
        public void TypedData_ReturnsDeserializedData()
        {
            // Arrange
            var data = JObject.FromObject(new { from = 1, to = 5 });
            var innerContext = new TestCommandContext("nowplayinglistmove", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Assert
            typedContext.TypedData.Should().NotBeNull();
            typedContext.TypedData.From.Should().Be(1);
            typedContext.TypedData.To.Should().Be(5);
        }

        #endregion

        #region IsValid - Successful Deserialization

        [Fact]
        public void IsValid_ReturnsTrue_WhenDeserializationSucceedsAndNoValidatable()
        {
            // Arrange - ProtocolHandshakeRequest doesn't implement IValidatable
            var data = JObject.FromObject(new { protocol_version = 5 });
            var innerContext = new TestCommandContext("protocol", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<ProtocolHandshakeRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.ProtocolVersion.Should().Be(5);
        }

        [Fact]
        public void IsValid_ReturnsTrue_WhenValidatableIsValid()
        {
            // Arrange - MoveTrackRequest implements IValidatable
            var data = JObject.FromObject(new { from = 0, to = 3 });
            var innerContext = new TestCommandContext("nowplayinglistmove", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
        }

        [Fact]
        public void IsValid_ReturnsFalse_WhenValidatableIsNotValid()
        {
            // Arrange - MoveTrackRequest with missing 'to' field
            var data = JObject.FromObject(new { from = 0 });
            var innerContext = new TestCommandContext("nowplayinglistmove", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeFalse();
            typedContext.TypedData.From.Should().Be(0);
            typedContext.TypedData.To.Should().BeNull();
        }

        #endregion

        #region IsValid - Failed Deserialization

        [Fact]
        public void IsValid_ReturnsFalse_WhenDeserializationFails()
        {
            // Arrange - passing incompatible data type
            var innerContext = new TestCommandContext("test", new object(), TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeFalse();
        }

        [Fact]
        public void IsValid_ReturnsFalse_WhenDataIsNull()
        {
            // Arrange
            var innerContext = new TestCommandContext("test", null, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeFalse();
        }

        [Fact]
        public void TypedData_IsDefault_WhenDeserializationFails()
        {
            // Arrange
            var innerContext = new TestCommandContext("test", "invalid", TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Assert
            typedContext.TypedData.Should().BeNull();
            typedContext.IsValid.Should().BeFalse();
        }

        #endregion

        #region Specific Request Type Tests

        [Fact]
        public void TagChangeRequest_IsValid_WhenTagProvided()
        {
            // Arrange
            var data = JObject.FromObject(new { tag = "rating", value = "5" });
            var innerContext = new TestCommandContext("nowplayingtagchange", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<TagChangeRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Tag.Should().Be("rating");
            typedContext.TypedData.Value.Should().Be("5");
        }

        [Fact]
        public void TagChangeRequest_IsNotValid_WhenTagMissing()
        {
            // Arrange
            var data = JObject.FromObject(new { value = "5" });
            var innerContext = new TestCommandContext("nowplayingtagchange", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<TagChangeRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeFalse();
        }

        [Fact]
        public void PaginationRequest_Deserializes_WithDefaults()
        {
            // Arrange
            var data = JObject.FromObject(new { });
            var innerContext = new TestCommandContext("playlistlist", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<PaginationRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Offset.Should().Be(0);
            typedContext.TypedData.Limit.Should().Be(PaginationRequest.DefaultLimit);
        }

        [Fact]
        public void PaginationRequest_Deserializes_WithCustomValues()
        {
            // Arrange
            var data = JObject.FromObject(new { offset = 50, limit = 25 });
            var innerContext = new TestCommandContext("playlistlist", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<PaginationRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Offset.Should().Be(50);
            typedContext.TypedData.Limit.Should().Be(25);
        }

        [Fact]
        public void SearchRequest_Deserializes_Correctly()
        {
            // Arrange
            var data = JObject.FromObject(new { type = "artist", query = "Beatles" });
            var innerContext = new TestCommandContext("librarysearch", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<SearchRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Type.Should().Be("artist");
            typedContext.TypedData.Query.Should().Be("Beatles");
        }

        [Fact]
        public void AlbumCoverRequest_Deserializes_WithInheritedPagination()
        {
            // Arrange
            var data = JObject.FromObject(new { offset = 10, limit = 20, album = "Abbey Road" });
            var innerContext = new TestCommandContext("libraryalbumcover", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<AlbumCoverRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Offset.Should().Be(10);
            typedContext.TypedData.Limit.Should().Be(20);
            typedContext.TypedData.Album.Should().Be("Abbey Road");
        }

        [Fact]
        public void BrowseArtistsRequest_Deserializes_WithAlbumArtistsFlag()
        {
            // Arrange
            var data = JObject.FromObject(new { album_artists = true, offset = 0, limit = 50 });
            var innerContext = new TestCommandContext("librarybrowseartists", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<BrowseArtistsRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.AlbumArtists.Should().BeTrue();
        }

        [Fact]
        public void QueueRequest_Deserializes_WithDataList()
        {
            // Arrange
            var data = JObject.FromObject(new { queue = "next", data = new[] { "track1", "track2" } });
            var innerContext = new TestCommandContext("nowplayingqueue", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<QueueRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Queue.Should().Be("next");
            typedContext.TypedData.Data.Should().HaveCount(2);
        }

        #endregion

        #region TryGetData Delegation

        [Fact]
        public void TryGetData_DelegatesToInnerContext()
        {
            // Arrange
            var data = JObject.FromObject(new { from = 1, to = 2 });
            var innerContext = new TestCommandContext("test", data, TestConnectionId);
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Act
            var result = typedContext.TryGetData<MoveTrackRequest>(out var value);

            // Assert
            result.Should().BeTrue();
            value.From.Should().Be(1);
            value.To.Should().Be(2);
        }

        [Fact]
        public void GetDataOrDefault_DelegatesToInnerContext()
        {
            // Arrange
            var innerContext = new TestCommandContext("test", 42, TestConnectionId);
            var typedContext = new TypedCommandContext<int>(innerContext);

            // Act
            var result = typedContext.GetDataOrDefault<int>();

            // Assert
            result.Should().Be(42);
        }

        [Fact]
        public void GetDataOrDefault_ReturnsDefault_WhenConversionFails()
        {
            // Arrange
            var innerContext = new TestCommandContext("test", "not-a-number", TestConnectionId);
            var typedContext = new TypedCommandContext<int>(innerContext);

            // Act
            var result = typedContext.GetDataOrDefault(-1);

            // Assert
            result.Should().Be(-1);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void TypedContext_WithEmptyJObject_IsValid()
        {
            // Arrange - empty JObject deserializes to default values
            var data = JObject.Parse("{}");
            var innerContext = new TestCommandContext("test", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<PaginationRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Should().NotBeNull();
        }

        [Fact]
        public void TypedContext_WithExtraProperties_IgnoresThem()
        {
            // Arrange
            var data = JObject.FromObject(new { from = 1, to = 2, extra = "ignored" });
            var innerContext = new TestCommandContext("test", data, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.From.Should().Be(1);
            typedContext.TypedData.To.Should().Be(2);
        }

        [Fact]
        public void TypedContext_DirectObjectMatch_Works()
        {
            // Arrange - when Data is already the correct type
            var request = new MoveTrackRequest { From = 5, To = 10 };
            var innerContext = new TestCommandContext("test", request, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<MoveTrackRequest>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.From.Should().Be(5);
            typedContext.TypedData.To.Should().Be(10);
        }

        [Fact]
        public void TypedContext_WithPrimitiveType_Works()
        {
            // Arrange
            var innerContext = new TestCommandContext("test", 123, TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<int>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Should().Be(123);
        }

        [Fact]
        public void TypedContext_WithStringType_Works()
        {
            // Arrange
            var innerContext = new TestCommandContext("test", "hello", TestConnectionId);

            // Act
            var typedContext = new TypedCommandContext<string>(innerContext);

            // Assert
            typedContext.IsValid.Should().BeTrue();
            typedContext.TypedData.Should().Be("hello");
        }

        #endregion
    }
}
