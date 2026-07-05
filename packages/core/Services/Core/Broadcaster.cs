using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Models.Commands;
using MusicBeePlugin.Protocol.Messages;

namespace MusicBeePlugin.Services.Core
{
    /// <summary>
    ///     Implementation of IBroadcaster using EventAggregator for messaging
    /// </summary>
    public class Broadcaster : IBroadcaster
    {
        private const string LyricsNotFoundMessage = "Lyrics Not Found";

        private readonly IEventAggregator _eventAggregator;

        public Broadcaster(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public void BroadcastCover(string cover)
        {
            var payload = new CoverPayload(cover, false);
            PublishBroadcast(ProtocolConstants.NowPlayingCover, cover, payload);
        }

        public void BroadcastLyrics(string lyrics)
        {
            var v2Data = !string.IsNullOrEmpty(lyrics) ? lyrics : LyricsNotFoundMessage;
            var v3Payload = new LyricsPayload(lyrics);
            PublishBroadcast(ProtocolConstants.NowPlayingLyrics, v2Data, v3Payload);
        }

        /// <summary>
        ///     Publishes a broadcast event with V2 and V3 payloads.
        /// </summary>
        private void PublishBroadcast(string messageType, object v2Payload, object v3Payload)
        {
            var broadcastEvent = new BroadcastEvent(messageType);
            broadcastEvent.AddPayload(ProtocolConstants.V2, v2Payload);
            broadcastEvent.AddPayload(ProtocolConstants.V3, v3Payload);
            _eventAggregator.Publish(broadcastEvent);
        }
    }
}
