using MusicBeePlugin.Events;
using MusicBeePlugin.Networking;

namespace MusicBeePlugin.Controller
{
    class CommunicationController
    {
        private static readonly CommunicationController ClassInstance = new CommunicationController();
        private SocketServer _server;
        private ProtocolHandler _pHandler;

        private CommunicationController()
        {
            _server = new SocketServer();
            _pHandler = new ProtocolHandler();
            _pHandler.ReplyAvailable += HandleReplyAvailable;
            _pHandler.DisconnectClient += HandleDisconnectClient;
            _server.ClientConnected += HandleClientConnected;
            _server.ClientDisconnected += HandleClientDisconnected;
        }

        private void HandleDisconnectClient(object sender, MessageEventArgs e)
        {
           _server.HandleDisconnectClient(sender,e);
        }

        private void HandleReplyAvailable(object sender, MessageEventArgs e)
        {
           _server.HandleReplyAvailable(sender,e);
        }

        private void HandleClientDisconnected(object sender, MessageEventArgs e)
        {
            _pHandler.HandleClientDisconnected(sender,e);
        }

        private void HandleClientConnected(object sender, MessageEventArgs e)
        {
           _pHandler.HandleClientConnected(sender,e);
        }

        public static CommunicationController Instance
        {
            get { return ClassInstance; }
        }

        public void StartSocket()
        {
            _server.Start();
        }

        public void StopSocket()
        {
            _server.Stop();
        }
    }
}
