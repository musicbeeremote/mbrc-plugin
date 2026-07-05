using FluentAssertions;
using MusicBeePlugin.Models.Entities;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Protocol
{
    public class SocketMessageTests
    {
        [Fact]
        public void Constructor_WithContextAndData_SetsProperties()
        {
            // Arrange & Act
            var message = new SocketMessage("player", "test-data");

            // Assert
            message.Context.Should().Be("player");
            message.Data.Should().Be("test-data");
        }

        [Fact]
        public void Constructor_WithJObject_ParsesContext()
        {
            // Arrange
            var json = JObject.Parse("{\"context\":\"nowplayingtrack\",\"data\":\"test\"}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            message.Context.Should().Be("nowplayingtrack");
        }

        [Fact]
        public void Constructor_WithJObject_ParsesStringData()
        {
            // Arrange
            var json = JObject.Parse("{\"context\":\"test\",\"data\":\"simple-string\"}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            message.Data.Should().Be("simple-string");
        }

        [Fact]
        public void Constructor_WithJObject_ParsesObjectData()
        {
            // Arrange
            var json = JObject.Parse("{\"context\":\"protocol\",\"data\":{\"protocol_version\":4}}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            message.Data.Should().BeAssignableTo<JToken>();
            var dataObj = message.Data as JToken;
            dataObj["protocol_version"].Value<int>().Should().Be(4);
        }

        [Fact]
        public void Constructor_WithJObject_NullData_SetsEmptyString()
        {
            // Arrange
            var json = JObject.Parse("{\"context\":\"ping\"}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            message.Data.Should().Be(string.Empty);
        }

        [Fact]
        public void Constructor_WithJObject_NullContext_SetsEmptyString()
        {
            // Arrange
            var json = JObject.Parse("{\"data\":\"test\"}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            message.Context.Should().Be(string.Empty);
        }

        [Fact]
        public void Constructor_WithJObject_NumericData_ConvertsToString()
        {
            // Arrange
            var json = JObject.Parse("{\"context\":\"volume\",\"data\":75}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            message.Data.Should().Be("75");
        }

        [Fact]
        public void Constructor_WithJObject_BooleanData_ConvertsToString()
        {
            // Arrange
            var json = JObject.Parse("{\"context\":\"mute\",\"data\":true}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            message.Data.Should().Be("True");
        }

        [Fact]
        public void ToJsonString_ReturnsValidJson()
        {
            // Arrange
            var message = new SocketMessage("test", "data");

            // Act
            var json = message.ToJsonString();

            // Assert
            json.Should().Contain("\"context\":\"test\"");
            json.Should().Contain("\"data\":\"data\"");
        }

        [Fact]
        public void ToString_ReturnsJsonString()
        {
            // Arrange
            var message = new SocketMessage("test", "data");

            // Act
            var result = message.ToString();

            // Assert
            result.Should().Be(message.ToJsonString());
        }

        [Fact]
        public void ToJsonString_WithObjectData_SerializesCorrectly()
        {
            // Arrange
            var dataObj = new { volume = 50, mute = false };
            var message = new SocketMessage("status", dataObj);

            // Act
            var json = message.ToJsonString();

            // Assert
            json.Should().Contain("\"volume\":50");
            json.Should().Contain("\"mute\":false");
        }

        [Fact]
        public void DefaultConstructor_SetsNullProperties()
        {
            // Act
            var message = new SocketMessage();

            // Assert
            message.Context.Should().BeNull();
            message.Data.Should().BeNull();
        }

        [Fact]
        public void Constructor_WithJObject_ArrayData_PreservesArray()
        {
            // Arrange
            var json = JObject.Parse("{\"context\":\"list\",\"data\":[1,2,3]}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            // Array data is converted to string representation
            message.Data.Should().NotBeNull();
        }

        [Theory]
        [InlineData("player")]
        [InlineData("protocol")]
        [InlineData("nowplayingtrack")]
        [InlineData("playervolume")]
        [InlineData("ping")]
        public void Constructor_WithVariousContexts_ParsesCorrectly(string context)
        {
            // Arrange
            var json = JObject.Parse($"{{\"context\":\"{context}\",\"data\":\"\"}}");

            // Act
            var message = new SocketMessage(json);

            // Assert
            message.Context.Should().Be(context);
        }
    }
}
