using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Entities;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    using Events;
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Xml;
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
                List<SocketMessage> msgList = new List<SocketMessage>();
                if (String.IsNullOrEmpty(incomingMessage) || incomingMessage == "\0\r\n")
                {
                    return;
                }
                try
                {
#if DEBUG
                    Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : Proccessing : " + incomingMessage + "\n");
                    
                    HttpHandler http = new HttpHandler();
                    if (http.IsHttpRequest(incomingMessage))
                    {
                        //OnReplyAvailable(new MessageEventArgs(http.GetHttpReply(), clientId));
                    }

#endif
                    
                    foreach (
                        string msg in
                            incomingMessage.Replace("\0","").Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        msgList.Add(JsonSerializer.DeserializeFromString<SocketMessage>(incomingMessage));
                    }
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

                    //EventBus.FireEvent(new MessageEvent(xmNode.Name, xmNode.InnerXml, clientId));

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