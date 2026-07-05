using MusicBeePlugin.Models.Entities;

namespace MusicBeePlugin.Protocol.Messages
{
    /// <summary>
    ///     Event for sending messages to clients via the new EventAggregator pattern.
    ///     Replaces the old ReplyAvailable MessageEvent pattern.
    /// </summary>
    public class MessageSendEvent
    {
        /// <summary>
        ///     Constructor for sending to specific client
        /// </summary>
        /// <param name="message">The message content to send</param>
        /// <param name="connectionId">Target connection ID</param>
        public MessageSendEvent(string message, string connectionId)
        {
            Message = message ?? string.Empty;
            ConnectionId = connectionId ?? "all";
        }

        /// <summary>
        ///     Constructor for broadcasting to all connections
        /// </summary>
        /// <param name="message">The message content to send</param>
        public MessageSendEvent(string message) : this(message, "all")
        {
        }

        /// <summary>
        ///     The message content to send (typically JSON string)
        /// </summary>
        public string Message { get; }

        /// <summary>
        ///     Target connection ID, or "all" for broadcast to all connections
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        ///     Create a MessageSendEvent from a SocketMessage for specific client
        /// </summary>
        /// <param name="socketMessage">The SocketMessage to send</param>
        /// <param name="connectionId">Target connection ID</param>
        /// <returns>MessageSendEvent ready for publishing</returns>
        public static MessageSendEvent FromSocketMessage(SocketMessage socketMessage, string connectionId)
        {
            var jsonMessage = socketMessage.ToJsonString();
            return new MessageSendEvent(jsonMessage, connectionId);
        }

        /// <summary>
        ///     Create a MessageSendEvent from a SocketMessage for broadcast
        /// </summary>
        /// <param name="socketMessage">The SocketMessage to send</param>
        /// <returns>MessageSendEvent ready for publishing</returns>
        public static MessageSendEvent FromSocketMessage(SocketMessage socketMessage)
        {
            var jsonMessage = socketMessage.ToJsonString();
            return new MessageSendEvent(jsonMessage);
        }

        /// <summary>
        ///     Create a MessageSendEvent with context and data for specific client
        /// </summary>
        /// <param name="context">Message context/type</param>
        /// <param name="data">Message data</param>
        /// <param name="connectionId">Target connection ID</param>
        /// <returns>MessageSendEvent ready for publishing</returns>
        public static MessageSendEvent Create(string context, object data, string connectionId)
        {
            var socketMessage = new SocketMessage(context, data);
            return FromSocketMessage(socketMessage, connectionId);
        }

        /// <summary>
        ///     Create a MessageSendEvent with context and data for broadcast
        /// </summary>
        /// <param name="context">Message context/type</param>
        /// <param name="data">Message data</param>
        /// <returns>MessageSendEvent ready for publishing</returns>
        public static MessageSendEvent Create(string context, object data)
        {
            var socketMessage = new SocketMessage(context, data);
            return FromSocketMessage(socketMessage);
        }
    }
}
