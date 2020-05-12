using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Network
{
    public class ProtocolHandler
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ITinyMessengerHub _hub;
        private readonly Authenticator _auth;

        public ProtocolHandler(ITinyMessengerHub hub, Authenticator auth)
        {
            _hub = hub;
            _auth = auth;
        }

        /// <summary>
        ///     Processes the incoming message and answer's sending back the needed data.
        /// </summary>
        /// <param name="incomingMessage">The incoming message.</param>
        /// <param name="connectionId"> The id of the connection that send the incoming message.</param>
        public void ProcessIncomingMessage(string incomingMessage, string connectionId)
        {
            _logger.Debug($"Received by client: {connectionId} message --> {incomingMessage}");

            if (string.IsNullOrEmpty(incomingMessage))
            {
                return;
            }

            try
            {
                var messageList = GetMessages(incomingMessage, connectionId);
                var connection = _auth.GetConnection(connectionId);

                foreach (var message in messageList)
                {
                    if (message.IsVerifyConnection())
                    {
                        var socketMessage = new SocketMessage(Constants.VerifyConnection, string.Empty);
                        _hub.Publish(new PluginResponseAvailableEvent(socketMessage, connectionId));
                        return;
                    }

                    if (connection.PacketNumber == 0 && !message.IsPlayer())
                    {
                        _hub.Publish(new ForceClientDisconnect(connectionId));
                        return;
                    }

                    if (connection.PacketNumber == 1 && !message.IsProtocol())
                    {
                        _hub.Publish(new ForceClientDisconnect(connectionId));
                        return;
                    }

                    if (message.IsProtocol() && message.Data is JObject)
                    {
                        HandleProtocolMessage(message, connection);
                    }

                    _hub.Publish(new MessageEvent(message.Context, message.Data, connectionId, connection.ClientId));
                }

                connection.IncreasePacketNumber();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Processing message failed --> {incomingMessage} from {connectionId}");
            }
        }

        private static List<SocketMessage> SplitIncomingPayload(string incomingMessage)
        {
            var messageList = new List<SocketMessage>();
            var individualMessages = incomingMessage.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            messageList.AddRange(
                from message in individualMessages
                where !message.Equals("\n", StringComparison.InvariantCultureIgnoreCase)
                select new SocketMessage(JObject.Parse(message)));
            return messageList;
        }

        private List<SocketMessage> GetMessages(string incomingMessage, string connectionId)
        {
            List<SocketMessage> messageList;
            try
            {
                messageList = SplitIncomingPayload(incomingMessage);
            }
            catch (Exception ex)
            {
                messageList = new List<SocketMessage>();
                _logger.Error(ex, $"while processing message -> {incomingMessage} from {connectionId}");
            }

            return messageList;
        }

        private void HandleProtocolMessage(SocketMessage msg, SocketConnection connection)
        {
            var data = (JObject)msg.Data;
            connection.BroadcastsEnabled = !(bool)data["no_broadcast"];
            connection.ClientProtocolVersion = (int)data["protocol_version"];
            connection.ClientId = (string)data["client_id"];

            if (string.IsNullOrEmpty(connection.ClientId))
            {
                _logger.Debug(CultureInfo.CurrentCulture, msg.Data);
            }

            _hub.Publish(new ConnectionReadyEvent(connection));
        }
    }
}
