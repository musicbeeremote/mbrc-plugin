using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Utilities;
using NLog;
using ServiceStack.Text;
using TinyMessenger;

namespace MusicBeePlugin.AndroidRemote.Networking
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
                        select new SocketMessage(JsonObject.Parse(msg)));
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

                    if (msg.Context == Constants.Protocol && msg.Data is JsonObject)
                    {
                        var data = (JsonObject) msg.Data;
                        connection.BroadcastsEnabled = !data.Get<bool>("no_broadcast");
                        connection.ClientProtocolVersion = data.Get<int>("protocol_version");
                        connection.ClientId = data.Get<string>("client_id");

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