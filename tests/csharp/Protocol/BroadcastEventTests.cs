using FluentAssertions;
using MusicBeePlugin.Protocol.Messages;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Protocol
{
    public class BroadcastEventTests
    {
        [Fact]
        public void Constructor_SetsContent()
        {
            // Act
            var broadcastEvent = new BroadcastEvent("nowplayingcover");

            // Assert
            broadcastEvent.ToString().Should().Contain("nowplayingcover");
        }

        [Fact]
        public void AddPayload_AddsMessageForVersion()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("test");

            // Act
            broadcastEvent.AddPayload(2, "v2-data");

            // Assert
            var message = broadcastEvent.GetMessage(2);
            message.Should().NotBeEmpty();
            message.Should().Contain("test");
        }

        [Fact]
        public void GetMessage_ReturnsV2Message_ForV2Client()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("context");
            broadcastEvent.AddPayload(2, "v2-payload");
            broadcastEvent.AddPayload(3, "v3-payload");

            // Act
            var message = broadcastEvent.GetMessage(2);

            // Assert
            message.Should().Contain("v2-payload");
            message.Should().NotContain("v3-payload");
        }

        [Fact]
        public void GetMessage_ReturnsV3Message_ForV3Client()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("context");
            broadcastEvent.AddPayload(2, "v2-payload");
            broadcastEvent.AddPayload(3, "v3-payload");

            // Act
            var message = broadcastEvent.GetMessage(3);

            // Assert
            message.Should().Contain("v3-payload");
            message.Should().NotContain("v2-payload");
        }

        [Fact]
        public void GetMessage_ReturnsV3Message_ForV4Client()
        {
            // Arrange - V4 client should get the highest available version (V3)
            var broadcastEvent = new BroadcastEvent("context");
            broadcastEvent.AddPayload(2, "v2-payload");
            broadcastEvent.AddPayload(3, "v3-payload");

            // Act
            var message = broadcastEvent.GetMessage(4);

            // Assert
            message.Should().Contain("v3-payload");
        }

        [Fact]
        public void GetMessage_ReturnsV2Message_ForV1Client()
        {
            // Arrange - V1 client should fall back to V2 (lowest available)
            var broadcastEvent = new BroadcastEvent("context");
            broadcastEvent.AddPayload(2, "v2-payload");
            broadcastEvent.AddPayload(3, "v3-payload");

            // Act
            var message = broadcastEvent.GetMessage(1);

            // Assert
            // Should get V2 as default fallback
            message.Should().Contain("v2-payload");
        }

        [Fact]
        public void GetMessage_ReturnsEmptyString_WhenNoPayloads()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("context");

            // Act
            var message = broadcastEvent.GetMessage(2);

            // Assert
            message.Should().BeEmpty();
        }

        [Fact]
        public void AddPayload_WithObjectPayload_SerializesCorrectly()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("status");
            var payload = new { volume = 50, mute = false };

            // Act
            broadcastEvent.AddPayload(2, payload);

            // Assert
            var message = broadcastEvent.GetMessage(2);
            message.Should().Contain("volume");
            message.Should().Contain("50");
        }

        [Fact]
        public void ToString_ContainsContent()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("nowplayinglyrics");

            // Act
            var result = broadcastEvent.ToString();

            // Assert
            result.Should().Contain("nowplayinglyrics");
        }

        [Fact]
        public void ToString_ContainsBroadcastMessages()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("test");
            broadcastEvent.AddPayload(2, "data");

            // Act
            var result = broadcastEvent.ToString();

            // Assert
            result.Should().Contain("BroadcastMessages");
        }

        [Fact]
        public void GetMessage_SelectsHighestApplicableVersion()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("test");
            broadcastEvent.AddPayload(2, "v2");
            broadcastEvent.AddPayload(3, "v3");
            broadcastEvent.AddPayload(4, "v4");

            // Act & Assert
            broadcastEvent.GetMessage(2).Should().Contain("v2");
            broadcastEvent.GetMessage(3).Should().Contain("v3");
            broadcastEvent.GetMessage(4).Should().Contain("v4");
            broadcastEvent.GetMessage(5).Should().Contain("v4"); // Falls back to highest
        }

        [Fact]
        public void AddPayload_WithNullPayload_StillCreatesMessage()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("test");

            // Act
            broadcastEvent.AddPayload(2, null);

            // Assert
            var message = broadcastEvent.GetMessage(2);
            message.Should().NotBeEmpty();
            message.Should().Contain("test");
        }

        [Fact]
        public void AddPayload_WithStringPayload_IncludesStringInMessage()
        {
            // Arrange
            var broadcastEvent = new BroadcastEvent("lyrics");

            // Act
            broadcastEvent.AddPayload(2, "Hello World Lyrics");

            // Assert
            var message = broadcastEvent.GetMessage(2);
            message.Should().Contain("Hello World Lyrics");
        }
    }
}
