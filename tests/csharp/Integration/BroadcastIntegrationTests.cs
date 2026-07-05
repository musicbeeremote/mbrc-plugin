using FluentAssertions;
using MusicBeePlugin.Models.Commands;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Utilities.Network;
using MusicBeeRemote.Core.Tests.Mocks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    /// Integration tests for broadcast delivery to clients with different protocol versions.
    ///
    /// These tests verify that:
    /// - V2 clients receive raw string payloads
    /// - V3+ clients receive wrapper objects (CoverPayload, LyricsPayload)
    /// - Multiple clients with different versions receive appropriate formats
    /// </summary>
    public class BroadcastIntegrationTests
    {
        private readonly Authenticator _authenticator;
        private readonly MockEventAggregator _eventAggregator;
        private readonly Broadcaster _broadcaster;

        public BroadcastIntegrationTests()
        {
            _authenticator = new Authenticator();
            _eventAggregator = new MockEventAggregator();
            _broadcaster = new Broadcaster(_eventAggregator);
        }

        #region Cover Broadcast Tests

        [Fact]
        public void BroadcastCover_CreatesEventWithV2AndV3Payloads()
        {
            // Act
            _broadcaster.BroadcastCover("base64coverdata");

            // Assert
            _eventAggregator.PublishedMessages.Should().ContainSingle();
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            broadcastEvent.Should().NotBeNull();

            // V2 gets raw string
            var v2Message = broadcastEvent.GetMessage(2);
            v2Message.Should().Contain("base64coverdata");

            // V3 gets CoverPayload object
            var v3Message = broadcastEvent.GetMessage(3);
            v3Message.Should().Contain("status");
            v3Message.Should().Contain("cover");
        }

        [Fact]
        public void BroadcastCover_V2Client_ReceivesRawBase64String()
        {
            // Arrange
            var coverData = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ";

            // Act
            _broadcaster.BroadcastCover(coverData);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(2);

            // V2 format: raw base64 string in data field
            message.Should().Contain(coverData);
            message.Should().NotContain("\"status\"");
        }

        [Fact]
        public void BroadcastCover_V3Client_ReceivesCoverPayloadObject()
        {
            // Arrange
            var coverData = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ";

            // Act
            _broadcaster.BroadcastCover(coverData);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(3);

            // V3 format: CoverPayload object with status=1 (cover ready, not included in broadcast)
            // Cover field is omitted when not included to match v1.4.1 behavior
            message.Should().Contain("\"status\":1");
            message.Should().NotContain("\"cover\"");
        }

        [Fact]
        public void BroadcastCover_V4Client_ReceivesV3Format()
        {
            // V4 clients should receive V3 format (highest available)
            // Arrange
            var coverData = "base64data";

            // Act
            _broadcaster.BroadcastCover(coverData);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var v3Message = broadcastEvent.GetMessage(3);
            var v4Message = broadcastEvent.GetMessage(4);

            // V4 should get same format as V3
            v4Message.Should().Be(v3Message);
        }

        [Fact]
        public void BroadcastCover_EmptyCover_V3ClientGets404Status()
        {
            // Act
            _broadcaster.BroadcastCover(string.Empty);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(3);

            // CoverPayload with empty cover sets status to 404
            message.Should().Contain("404");
        }

        #endregion

        #region Lyrics Broadcast Tests

        [Fact]
        public void BroadcastLyrics_CreatesEventWithV2AndV3Payloads()
        {
            // Act
            _broadcaster.BroadcastLyrics("These are the lyrics");

            // Assert
            _eventAggregator.PublishedMessages.Should().ContainSingle();
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            broadcastEvent.Should().NotBeNull();
        }

        [Fact]
        public void BroadcastLyrics_V2Client_ReceivesRawLyricsString()
        {
            // Arrange
            var lyrics = "Verse 1 Chorus Verse 2";

            // Act
            _broadcaster.BroadcastLyrics(lyrics);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(2);

            // V2 format: raw lyrics string
            message.Should().Contain(lyrics);
            message.Should().NotContain("\"status\"");
        }

        [Fact]
        public void BroadcastLyrics_V3Client_ReceivesLyricsPayloadObject()
        {
            // Arrange
            var lyrics = "Verse 1 Chorus Verse 2";

            // Act
            _broadcaster.BroadcastLyrics(lyrics);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(3);

            // V3 format: LyricsPayload object with status and lyrics fields
            message.Should().Contain("\"status\"");
            message.Should().Contain("\"lyrics\"");
            message.Should().Contain("200"); // Success status
        }

        [Fact]
        public void BroadcastLyrics_EmptyLyrics_V2ClientGetsNotFoundMessage()
        {
            // Act
            _broadcaster.BroadcastLyrics(string.Empty);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(2);

            // V2 format: "Lyrics Not Found" message
            message.Should().Contain("Lyrics Not Found");
        }

        [Fact]
        public void BroadcastLyrics_EmptyLyrics_V3ClientGets404Status()
        {
            // Act
            _broadcaster.BroadcastLyrics(string.Empty);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(3);

            // LyricsPayload with empty lyrics sets status to 404
            message.Should().Contain("404");
        }

        [Fact]
        public void BroadcastLyrics_NullLyrics_V2ClientGetsNotFoundMessage()
        {
            // Act
            _broadcaster.BroadcastLyrics(null);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(2);

            message.Should().Contain("Lyrics Not Found");
        }

        #endregion

        #region Version Selection Tests

        [Theory]
        [InlineData(2, false)] // V2 gets raw format (no status field)
        [InlineData(3, true)]  // V3 gets payload object (has status field)
        [InlineData(4, true)]  // V4 gets payload object (has status field)
        public void BroadcastCover_ClientVersionDeterminesFormat(int version, bool expectsPayloadObject)
        {
            // Arrange
            _broadcaster.BroadcastCover("coverdata");

            // Act
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(version);

            // Assert
            if (expectsPayloadObject)
            {
                message.Should().Contain("\"status\"");
            }
            else
            {
                message.Should().NotContain("\"status\"");
            }
        }

        [Theory]
        [InlineData(2, false)] // V2 gets raw format
        [InlineData(3, true)]  // V3 gets payload object
        [InlineData(4, true)]  // V4 gets payload object
        public void BroadcastLyrics_ClientVersionDeterminesFormat(int version, bool expectsPayloadObject)
        {
            // Arrange
            _broadcaster.BroadcastLyrics("test lyrics");

            // Act
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(version);

            // Assert
            if (expectsPayloadObject)
            {
                message.Should().Contain("\"status\"");
                message.Should().Contain("\"lyrics\"");
            }
            else
            {
                message.Should().NotContain("\"status\"");
            }
        }

        #endregion

        #region BroadcastEvent GetMessage Selection Tests

        [Fact]
        public void BroadcastEvent_SelectsCorrectVersionForClient()
        {
            // Arrange - Create event with multiple version payloads
            var broadcastEvent = new BroadcastEvent("test");
            broadcastEvent.AddPayload(2, "v2-payload");
            broadcastEvent.AddPayload(3, new { version = 3, data = "v3-payload" });

            // Assert - Each client version gets appropriate payload
            broadcastEvent.GetMessage(2).Should().Contain("v2-payload");
            broadcastEvent.GetMessage(3).Should().Contain("v3-payload");
            broadcastEvent.GetMessage(4).Should().Contain("v3-payload"); // Falls back to V3
        }

        [Fact]
        public void BroadcastEvent_V21Client_GetsV2Payload()
        {
            // Arrange - V2.1 maps to integer 2, should get V2 payload
            var broadcastEvent = new BroadcastEvent("test");
            broadcastEvent.AddPayload(2, "v2-data");
            broadcastEvent.AddPayload(3, "v3-data");

            // Act - V2.1 client has protocol version 2 (integer part)
            var message = broadcastEvent.GetMessage(2);

            // Assert
            message.Should().Contain("v2-data");
        }

        #endregion

        #region Full Broadcast Delivery Simulation

        [Fact]
        public void SimulateBroadcastDelivery_MultipleClientsReceiveCorrectFormat()
        {
            // Arrange - Set up clients with different protocol versions
            _authenticator.AddClientOnConnect("v2-client");
            _authenticator.AddClientOnConnect("v3-client");
            _authenticator.AddClientOnConnect("v4-client");

            var v2Client = _authenticator.Client("v2-client");
            var v3Client = _authenticator.Client("v3-client");
            var v4Client = _authenticator.Client("v4-client");

            v2Client.ClientProtocolVersion = 2;
            v3Client.ClientProtocolVersion = 3;
            v4Client.ClientProtocolVersion = 4;

            // Act - Broadcast cover
            _broadcaster.BroadcastCover("coverdata");

            // Assert - Verify each client would receive correct format
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;

            var v2Message = broadcastEvent.GetMessage(v2Client.ClientProtocolVersion);
            var v3Message = broadcastEvent.GetMessage(v3Client.ClientProtocolVersion);
            var v4Message = broadcastEvent.GetMessage(v4Client.ClientProtocolVersion);

            // V2 client gets raw format
            v2Message.Should().NotContain("\"status\"");

            // V3 and V4 clients get payload object format
            v3Message.Should().Contain("\"status\"");
            v4Message.Should().Contain("\"status\"");
            v3Message.Should().Be(v4Message); // Same format
        }

        [Fact]
        public void SimulateBroadcastDelivery_LegacyV21Client_GetsV2Format()
        {
            // Arrange - V2.1 client (maps to integer 2 but with enhanced capabilities)
            _authenticator.AddClientOnConnect("v21-client");
            var client = _authenticator.Client("v21-client");
            client.ClientProtocolVersion = 2; // Integer part of 2.1
            client.SetCapabilitiesFromVersion(2.1);

            // Act
            _broadcaster.BroadcastLyrics("test lyrics");

            // Assert - V2.1 still gets V2 broadcast format (based on integer version)
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(client.ClientProtocolVersion);

            // V2 format (no status wrapper)
            message.Should().NotContain("\"status\"");
            message.Should().Contain("test lyrics");
        }

        #endregion

        #region Serialized Message Structure Tests

        [Fact]
        public void BroadcastCover_V2_SerializedMessageHasCorrectStructure()
        {
            // Arrange
            var coverData = "base64coverdata";

            // Act
            _broadcaster.BroadcastCover(coverData);

            // Assert - Parse and verify JSON structure
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var jsonMessage = broadcastEvent.GetMessage(2);
            var parsed = JObject.Parse(jsonMessage);

            // V2 message structure: {"context":"nowplayingcover","data":"base64coverdata"}
            parsed["context"].Value<string>().Should().Be("nowplayingcover");
            parsed["data"].Value<string>().Should().Be(coverData);
        }

        [Fact]
        public void BroadcastCover_V3_SerializedMessageHasCorrectStructure()
        {
            // Arrange
            var coverData = "base64coverdata";

            // Act
            _broadcaster.BroadcastCover(coverData);

            // Assert - Parse and verify JSON structure
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var jsonMessage = broadcastEvent.GetMessage(3);
            var parsed = JObject.Parse(jsonMessage);

            // V3 message structure: {"context":"nowplayingcover","data":{"status":1}}
            // Note: Broadcaster uses CoverPayload(cover, false) which sets status=1 without cover field
            parsed["context"].Value<string>().Should().Be("nowplayingcover");
            parsed["data"].Should().BeOfType<JObject>();

            var dataObj = parsed["data"] as JObject;
            dataObj["status"].Should().NotBeNull();
            dataObj["status"].Value<int>().Should().Be(1);
            dataObj["cover"].Should().BeNull(); // cover field not included when not sending cover data
        }

        [Fact]
        public void BroadcastLyrics_V2_SerializedMessageHasCorrectStructure()
        {
            // Arrange
            var lyrics = "Test lyrics content";

            // Act
            _broadcaster.BroadcastLyrics(lyrics);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var jsonMessage = broadcastEvent.GetMessage(2);
            var parsed = JObject.Parse(jsonMessage);

            // V2 message structure: {"context":"nowplayinglyrics","data":"Test lyrics content"}
            parsed["context"].Value<string>().Should().Be("nowplayinglyrics");
            parsed["data"].Value<string>().Should().Be(lyrics);
        }

        [Fact]
        public void BroadcastLyrics_V3_SerializedMessageHasCorrectStructure()
        {
            // Arrange
            var lyrics = "Test lyrics content";

            // Act
            _broadcaster.BroadcastLyrics(lyrics);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var jsonMessage = broadcastEvent.GetMessage(3);
            var parsed = JObject.Parse(jsonMessage);

            // V3 message structure: {"context":"nowplayinglyrics","data":{"status":200,"lyrics":"..."}}
            parsed["context"].Value<string>().Should().Be("nowplayinglyrics");
            parsed["data"].Should().BeOfType<JObject>();

            var dataObj = parsed["data"] as JObject;
            dataObj["status"].Value<int>().Should().Be(200);
            dataObj["lyrics"].Value<string>().Should().Be(lyrics);
        }

        [Fact]
        public void BroadcastLyrics_Empty_V3_SerializedMessageHas404Status()
        {
            // Act
            _broadcaster.BroadcastLyrics(string.Empty);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var jsonMessage = broadcastEvent.GetMessage(3);
            var parsed = JObject.Parse(jsonMessage);

            var dataObj = parsed["data"] as JObject;
            dataObj["status"].Value<int>().Should().Be(404);
            dataObj["lyrics"].Value<string>().Should().BeEmpty();
        }

        [Fact]
        public void BroadcastLyrics_Empty_V2_SerializedMessageHasNotFoundText()
        {
            // Act
            _broadcaster.BroadcastLyrics(string.Empty);

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var jsonMessage = broadcastEvent.GetMessage(2);
            var parsed = JObject.Parse(jsonMessage);

            parsed["data"].Value<string>().Should().Be("Lyrics Not Found");
        }

        #endregion

        #region Context/Command Name Tests

        [Fact]
        public void BroadcastCover_UsesCorrectContext()
        {
            // Act
            _broadcaster.BroadcastCover("data");

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(2);

            message.Should().Contain("nowplayingcover");
        }

        [Fact]
        public void BroadcastLyrics_UsesCorrectContext()
        {
            // Act
            _broadcaster.BroadcastLyrics("data");

            // Assert
            var broadcastEvent = _eventAggregator.PublishedMessages[0] as BroadcastEvent;
            var message = broadcastEvent.GetMessage(2);

            message.Should().Contain("nowplayinglyrics");
        }

        #endregion

        #region CoverPayload Tests

        [Fact]
        public void CoverPayload_WithCover_HasStatus200()
        {
            // Arrange & Act
            var payload = new CoverPayload("base64data", true);

            // Assert
            payload.Status.Should().Be(200);
            payload.Cover.Should().Be("base64data");
        }

        [Fact]
        public void CoverPayload_WithCoverNotIncluded_HasStatus1()
        {
            // Arrange & Act - Cover exists but not included in payload
            var payload = new CoverPayload("base64data", false);

            // Assert
            payload.Status.Should().Be(1); // CoverReady
            payload.Cover.Should().BeNull(); // Not serialized when null
        }

        [Fact]
        public void CoverPayload_NoCover_HasStatus404()
        {
            // Arrange & Act
            var payload = new CoverPayload(string.Empty, true);

            // Assert
            payload.Status.Should().Be(404);
            payload.Cover.Should().BeNull(); // Not serialized when null
        }

        #endregion

        #region LyricsPayload Tests

        [Fact]
        public void LyricsPayload_WithLyrics_HasStatus200()
        {
            // Arrange & Act
            var payload = new LyricsPayload("Some lyrics here");

            // Assert
            payload.Status.Should().Be(200);
            payload.Lyrics.Should().Be("Some lyrics here");
        }

        [Fact]
        public void LyricsPayload_NoLyrics_HasStatus404()
        {
            // Arrange & Act
            var payload = new LyricsPayload(string.Empty);

            // Assert
            payload.Status.Should().Be(404);
        }

        [Fact]
        public void LyricsPayload_NullLyrics_HasStatus404()
        {
            // Arrange & Act
            var payload = new LyricsPayload(null);

            // Assert
            payload.Status.Should().Be(404);
        }

        #endregion
    }
}
