using System;
using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.AndroidRemote.Entities;

using MusicBeePlugin.AndroidRemote.Events;
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
        /// <param name="clientId"> </param>
        public void ProcessIncomingMessage(string incomingMessage, string clientId)
        {
            _logger.Debug($"Received by client: {clientId} message --> {incomingMessage}");

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
                        in incomingMessage.Replace("\0", "")
                            .Split(new[] {"\r\n"},
                                StringSplitOptions.RemoveEmptyEntries)
                        where !msg.Equals("\n")
                        select new SocketMessage(JsonObject.Parse(msg)));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"while processing message -> {incomingMessage} from {clientId}");
                }

                var client = Authenticator.Client(clientId);

                foreach (var msg in msgList)
                {
                    if (client.PacketNumber == 0 && msg.Context != Constants.Player)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.ActionForceClientDisconnect, string.Empty,
                            clientId));
                        return;
                    }

                    if (client.PacketNumber == 1 && msg.Context != Constants.Protocol)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.ActionForceClientDisconnect, string.Empty,
                            clientId));
                        return;
                    }

                    if (msg.Context == Constants.Protocol && msg.Data is JsonObject)
                    {
                        var data = (JsonObject) msg.Data;
                        client.BroadcastsEnabled = !data.Get<bool>("no_broadcast");
                        client.ClientProtocolVersion = data.Get<int>("protocol_version");
                    }

                    EventBus.FireEvent(new MessageEvent(msg.Context, msg.Data, clientId));
                }
                client.IncreasePacketNumber();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Processing message failed --> {incomingMessage} from {clientId}");
            }
        }
    }
}