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
        private readonly XmlDocument xmlDoc;

        public ProtocolHandler()
        {
            xmlDoc = new XmlDocument();
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
                    xmlDoc.LoadXml(XmlCreator.Create("incoming", incomingMessage.Replace("\0", ""), false, false));
                }
                catch(XmlException xmex)
                {
#if DEBUG
                    Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : Xmlexception : " + xmex + " : while processing : " + incomingMessage + "\n");
#endif               
                    return;
                }
                catch (Exception ex)
                {
#if DEBUG
                    ErrorHandler.LogError(ex);
                    ErrorHandler.LogValue(incomingMessage);
                    Debug.WriteLine(DateTime.Now.ToString(CultureInfo.InvariantCulture) + " : Exception at : " +
                                    incomingMessage + "\n");
#endif               
                    // Xml load has probably failed and so there is no reason 
                }
                XmlNodeList nodeList = xmlDoc.FirstChild.ChildNodes;

                foreach (XmlNode xmNode in nodeList)
                {
                    if (Authenticator.Client(clientId).PacketNumber == 0 && xmNode.Name != Constants.Player)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.ActionForceClientDisconnect, string.Empty, clientId));
                        return;
                    }
                    if (Authenticator.Client(clientId).PacketNumber == 1 && xmNode.Name != Constants.Protocol)
                    {
                        EventBus.FireEvent(new MessageEvent(EventType.ActionForceClientDisconnect, string.Empty, clientId));
                        return;
                    }
                    EventBus.FireEvent(new MessageEvent(xmNode.Name, xmNode.InnerText, clientId));
                    
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