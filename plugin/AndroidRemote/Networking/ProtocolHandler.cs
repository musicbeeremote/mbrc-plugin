using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Utilities;
using NLog;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    internal class ProtocolHandler
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        ///     Processes the incoming message and answer's sending back the needed data.
        /// </summary>
        /// <param name="incomingMessage">The incoming message.</param>
        /// <param name="connectionId"> </param>
        public void ProcessIncomingMessage(string incomingMessage, string connectionId)
        {
            _logger.Debug($"Received by client: {connectionId} message --> {incomingMessage}");

            try
            {
                var msgList = new List<SocketMessage>();
                if (string.IsNullOrEmpty(incomingMessage))
                {
                    return;
                }
                try
                {
                    msgList.AddRange(from msg
                        in incomingMessage
                            .Split(new[] {"\r\n"},
                                StringSplitOptions.RemoveEmptyEntries)
                        where !msg.Equals("\n")
                        select new SocketMessage(JsonObject.Parse(msg)));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"while processing message -> {incomingMessage} from {connectionId}");
                }

                var connection = Authenticator.GetConnection(connectionId);

                foreach (var msg in msgList)
                {
                    if (msg.Context == Constants.VerifyConnection)
                    {
                        EventBus.FireEvent(
                            new MessageEvent(EventType.ReplyAvailable,
                                new SocketMessage(Constants.VerifyConnection,
                                    string.Empty).ToJsonString(), connectionId));
                        return;
                    }

                    if (connection.PacketNumber == 0 && msg.Context != Constants.Player)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.ActionForceClientDisconnect, string.Empty,
                            connectionId));
                        return;
                    }

                    if (connection.PacketNumber == 1 && msg.Context != Constants.Protocol)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.ActionForceClientDisconnect, string.Empty,
                            connectionId));
                        return;
                    }

                    if (msg.Context == Constants.Protocol && msg.Data is JsonObject)
                    {
                        var data = (JsonObject) msg.Data;
                        connection.BroadcastsEnabled = !data.Get<bool>("no_broadcast");
                        connection.ClientProtocolVersion = data.Get<int>("protocol_version");
                        connection.ClientId = data.Get<string>("client_id");

                        if (string.IsNullOrEmpty(connection.ClientId))
                        {

                        }
                    }

                    EventBus.FireEvent(new MessageEvent(msg.Context, msg.Data, connectionId));
                }
                connection.IncreasePacketNumber();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Processing message failed --> {incomingMessage} from {connectionId}");
            }
        }
    }
}