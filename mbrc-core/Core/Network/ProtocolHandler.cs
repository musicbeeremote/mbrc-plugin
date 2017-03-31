using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.Events;
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
                        select new SocketMessage(JObject.Parse(msg)));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"while processing message -> {incomingMessage} from {connectionId}");
                }

                var connection = _auth.GetConnection(connectionId);

                foreach (var msg in msgList)
                {
                    if (msg.Context == Constants.VerifyConnection)
                    {
                        var socketMessage = new SocketMessage(Constants.VerifyConnection, string.Empty);
                        _hub.Publish(new PluginResponseAvailableEvent(socketMessage, connectionId));
                        return;
                    }

                    if (connection.PacketNumber == 0 && msg.Context != Constants.Player)
                    {
                        _hub.Publish(new ForceClientDisconnect(connectionId));
                        return;
                    }

                    if (connection.PacketNumber == 1 && msg.Context != Constants.Protocol)
                    {
                        _hub.Publish(new ForceClientDisconnect(connectionId));
                        return;
                    }

                    if (msg.Context == Constants.Protocol && msg.Data is JObject)
                    {
                        var data = (JObject) msg.Data;
                        connection.BroadcastsEnabled = !(bool) data["no_broadcast"];
                        connection.ClientProtocolVersion = (int) data["protocol_version"];
                        connection.ClientId = (string) data["client_id"];

                        if (string.IsNullOrEmpty(connection.ClientId))
                        {
                            _logger.Debug(msg.Data);
                        }

                        _hub.Publish(new ConnectionReadyEvent(connection));
                    }

                    _hub.Publish(new MessageEvent(msg.Context, msg.Data, connectionId, connection.ClientId));
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