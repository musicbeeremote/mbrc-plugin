using System;
using System.Collections.Generic;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Infrastructure;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Utilities.Network;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin.Protocol.Processing
{
    /// <summary>
    ///     Optimized ProtocolHandler with improved parsing performance
    /// </summary>
    public class ProtocolHandler : IProtocolHandler
    {
        // Cached string comparisons
        private const string VerifyConnection = ProtocolConstants.VerifyConnection;
        private const string Player = ProtocolConstants.Player;
        private const string Protocol = ProtocolConstants.Protocol;
        private readonly IAuthenticator _authenticator;
        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IEventAggregator _eventBus;
        private readonly IPluginLogger _logger;

        // Pre-allocated collections to reduce GC pressure
        private readonly List<SocketMessage> _messageBuffer = new List<SocketMessage>(4);
        private readonly char[] _splitBuffer = { '\r', '\n' };

        public ProtocolHandler(
            IPluginLogger logger,
            IAuthenticator authenticator,
            IEventAggregator eventBus,
            ICommandDispatcher commandDispatcher)
        {
            _logger = logger;
            _authenticator = authenticator;
            _eventBus = eventBus;
            _commandDispatcher = commandDispatcher;
        }

        public event Action<string> ForceClientDisconnect;

        /// <summary>
        ///     Gets a short identifier for logging purposes.
        /// </summary>
        private string GetShortId(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return "?";

            var client = _authenticator.Client(connectionId);
            if (client != null)
                return client.ShortId;

            return connectionId.Length > 6 ? connectionId.Substring(0, 6) : connectionId;
        }

        /// <summary>
        ///     Processes the incoming message with optimized parsing
        /// </summary>
        /// <param name="incomingMessage">The incoming message.</param>
        /// <param name="connectionId">Client identifier</param>
        public void ProcessIncomingMessage(string incomingMessage, string connectionId)
        {
            if (string.IsNullOrEmpty(incomingMessage))
                return;

            _logger.Debug($"Received by {GetShortId(connectionId)} --> {incomingMessage}");

            try
            {
                // Fast path for single messages (most common case)
                if (incomingMessage.IndexOf("\r", StringComparison.Ordinal) == -1 && incomingMessage.IndexOf("\n", StringComparison.Ordinal) == -1)
                {
                    ProcessSingleMessage(incomingMessage, connectionId);
                    return;
                }

                // Multi-message path with optimized parsing
                ProcessMultipleMessages(incomingMessage, connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Processing message failed --> {incomingMessage} from {GetShortId(connectionId)}");
            }
        }

        private void ProcessSingleMessage(string messageText, string connectionId)
        {
            if (string.IsNullOrWhiteSpace(messageText))
                return;

            try
            {
                var socketMessage = ParseSocketMessage(messageText);
                if (socketMessage != null)
                    ProcessSocketMessage(socketMessage, connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to parse single message: {messageText} from {GetShortId(connectionId)}");
            }
        }

        private void ProcessMultipleMessages(string incomingMessage, string connectionId)
        {
            _messageBuffer.Clear();

            try
            {
                // Split and parse messages more efficiently
                var messages = incomingMessage.Split(_splitBuffer, StringSplitOptions.RemoveEmptyEntries);

                for (var i = 0; i < messages.Length; i++)
                {
                    var messageText = messages[i];
                    if (string.IsNullOrWhiteSpace(messageText))
                        continue;

                    var socketMessage = ParseSocketMessage(messageText);
                    if (socketMessage != null)
                        _messageBuffer.Add(socketMessage);
                }

                // Process all parsed messages
                for (var i = 0; i < _messageBuffer.Count; i++)
                    ProcessSocketMessage(_messageBuffer[i], connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process multiple messages from {GetShortId(connectionId)}");
            }
        }

        private OptimizedSocketMessage ParseSocketMessage(string messageText)
        {
            try
            {
                // Fast JSON validation - check for basic structure
                if (messageText.Length < 2 || messageText[0] != '{' || messageText[messageText.Length - 1] != '}')
                    return null;

                return new OptimizedSocketMessage(messageText);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to parse message: {messageText} - {ex.Message}");
                return null;
            }
        }

        private void ProcessSocketMessage(SocketMessage msg, string connectionId)
        {
            // Quick string comparison using reference equality where possible
            if (ReferenceEquals(msg.Context, VerifyConnection) || msg.Context == VerifyConnection)
            {
                var message = MessageSendEvent.Create(VerifyConnection, string.Empty, connectionId);
                _eventBus.PublishAsync(message);
                return;
            }

            var client = _authenticator.Client(connectionId);

            // Early validation checks
            if (client.PacketNumber == 0 && msg.Context != Player)
            {
                ForceClientDisconnect?.Invoke(connectionId);
                return;
            }

            if (client.PacketNumber == 1 && msg.Context != Protocol)
            {
                ForceClientDisconnect?.Invoke(connectionId);
                return;
            }

            // Protocol-specific handling is now delegated to SystemCommands.HandleProtocol
            // This section only validates the handshake sequence but doesn't modify client properties

            // Dispatch command
            var messageContext = new MessageContext(msg.Context, msg.Data, connectionId, client.ShortId);
            _commandDispatcher.Execute(messageContext);

            client.IncreasePacketNumber();
        }
    }

    /// <summary>
    ///     Optimized SocketMessage with faster parsing
    /// </summary>
    internal sealed class OptimizedSocketMessage : SocketMessage
    {
        public OptimizedSocketMessage(string jsonText)
        {
            ParseJson(jsonText);
        }

        private void ParseJson(string jsonText)
        {
            // Use Newtonsoft.Json for reliable parsing
            var jsonObj = JObject.Parse(jsonText);

            // Direct property access with null safety
            Context = jsonObj["context"]?.ToString() ?? string.Empty;

            var dataToken = jsonObj["data"];
            if (dataToken == null)
                Data = string.Empty;
            else if (dataToken.Type == JTokenType.Object)
                // Pass JObject directly - much cleaner than conversions
                Data = dataToken;
            else
                // Simple string/primitive data
                Data = dataToken.ToString();
        }
    }
}
