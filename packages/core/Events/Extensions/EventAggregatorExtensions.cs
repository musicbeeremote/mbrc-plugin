using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Protocol.Messages;

namespace MusicBeePlugin.Events.Extensions
{
    /// <summary>
    ///     Extension methods for IEventAggregator to simplify common messaging patterns.
    /// </summary>
    public static class EventAggregatorExtensions
    {
        /// <summary>
        ///     Publishes a message send event to a specific client.
        /// </summary>
        /// <param name="eventAggregator">The event aggregator instance.</param>
        /// <param name="messageType">The protocol message type.</param>
        /// <param name="data">The data payload to send.</param>
        /// <param name="connectionId">The target client connection ID.</param>
        public static void PublishMessage(this IEventAggregator eventAggregator, string messageType, object data, string connectionId)
        {
            var message = MessageSendEvent.Create(messageType, data, connectionId);
            eventAggregator.Publish(message);
        }

        /// <summary>
        ///     Publishes a message send event to a specific client with no data payload.
        /// </summary>
        /// <param name="eventAggregator">The event aggregator instance.</param>
        /// <param name="messageType">The protocol message type.</param>
        /// <param name="connectionId">The target client connection ID.</param>
        public static void PublishMessage(this IEventAggregator eventAggregator, string messageType, string connectionId)
        {
            PublishMessage(eventAggregator, messageType, null, connectionId);
        }
    }
}
