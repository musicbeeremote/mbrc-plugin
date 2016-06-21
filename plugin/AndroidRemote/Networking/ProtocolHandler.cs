using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Error;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Utilities;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    internal class ProtocolHandler
    {
        /// <summary>
        ///     Processes the incoming message and answer's sending back the needed data.
        /// </summary>
        /// <param name="incomingMessage">The incoming message.</param>
        /// <param name="clientId"> </param>
        public void ProcessIncomingMessage(string incomingMessage, string clientId)
        {
#if DEBUG
            ErrorHandler.VerboseValue("Incoming --> " + incomingMessage);
#endif
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
#if DEBUG
                    ErrorHandler.LogError(ex);
                    ErrorHandler.LogValue(incomingMessage);

                    Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : Exception at : " +
                                    incomingMessage + "\n");
#endif
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

                    if (msg.Context == Constants.Protocol && msg.Data.Equals(Constants.NoBroadcast))
                    {
                        client.BroadcastsEnabled = false;
                        client.ClientProtocolVersion = 2.2f;
                    }

                    EventBus.FireEvent(new MessageEvent(msg.Context, msg.Data, clientId));
                }
                client.IncreasePacketNumber();
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
            }
        }
    }
}