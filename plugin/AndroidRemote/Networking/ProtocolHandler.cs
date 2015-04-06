using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.AndroidRemote.Entities;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    using Events;
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using Error;
    using Utilities;

    internal class ProtocolHandler
    {


        public ProtocolHandler()
        {
         
        }

        /// <summary>
        /// Processes the incoming message and answer's sending back the needed data.
        /// </summary>
        /// <param name="incomingMessage">The incoming message.</param>
        /// <param name="clientId"> </param>
        public void ProcessIncomingMessage(string incomingMessage, string clientId)
        {
            try
            {
                var msgList = new List<SocketMessage>();
                if (string.IsNullOrEmpty(incomingMessage))
                {
                    return;
                }
                try
                {
#if DEBUG
                    Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : Proccessing : " + incomingMessage + "\n");
                
#endif

                    msgList.AddRange(from msg in incomingMessage.Replace("\0", "")
                                     .Split(new[] {"\r\n"}, StringSplitOptions.RemoveEmptyEntries) where !msg.Equals("\n") select new SocketMessage(JsonObject.Parse(msg)));
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

                foreach (SocketMessage msg in msgList)
                {
                    if (Authenticator.Client(clientId).PacketNumber == 0 && msg.context != Constants.Player)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.ActionForceClientDisconnect, string.Empty, clientId));
                        return;
                    }
                    if (Authenticator.Client(clientId).PacketNumber == 1 && msg.context != Constants.Protocol)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.ActionForceClientDisconnect, string.Empty, clientId));
                        return;
                    }

                    EventBus.FireEvent(new MessageEvent(msg.context, msg.data, clientId));
                }
                Authenticator.Client(clientId).IncreasePacketNumber();
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("Exception: " + ex);
                ErrorHandler.LogError(ex);
#endif
            }
        }
    }
}