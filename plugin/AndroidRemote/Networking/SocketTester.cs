using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Settings;
using NLog;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    public class SocketTester
    {

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        // State object for receiving data from remote device.
        public class StateObject
        {
            // Client socket.
            public Socket WorkSocket;

            // Size of receive buffer.
            public const int BufferSize = 256;

            // Receive buffer.
            public byte[] Buffer = new byte[BufferSize];

            // Received data string.
            public StringBuilder Sb = new StringBuilder();
        }

        public IConnectionListener ConnectionListener { get; set; }

        public void VerifyConnection()
        {
            try
            {
                var port = UserSettings.Instance.ListeningPort;
                var ipEndpoint = new IPEndPoint(IPAddress.Loopback, (int) port);
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                client.BeginConnect(ipEndpoint, ConnectCallback, client);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Debug, e, "Tester Connection error");
                ConnectionListener?.OnConnectionResult(false);
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                var state = (StateObject) ar.AsyncState;
                var client = state.WorkSocket;
                var received = client.EndReceive(ar);
                var chars = new char[received + 1];
                var decoder = Encoding.UTF8.GetDecoder();
                decoder.GetChars(state.Buffer, 0, received, chars, 0);
                var message = new string(chars);
                var json = JsonObject.Parse(message);
                var verified = json.Get("context") == Constants.VerifyConnection;

                _logger.Log(LogLevel.Info, $"Connection verified: {verified}");
                ConnectionListener?.OnConnectionResult(verified);

                client.Shutdown(SocketShutdown.Both);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Debug, e, "Tester Connection error");
                ConnectionListener?.OnConnectionResult(false);
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                var client = (Socket) ar.AsyncState;
                client.EndConnect(ar);
                var state = new StateObject {WorkSocket = client};
                client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
                client.ReceiveTimeout = 3000;
                client.Send(Payload());
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Debug, e, "Tester Connection error");
                ConnectionListener?.OnConnectionResult(false);
            }
        }

        private static byte[] Payload()
        {
            var socketMessage = new SocketMessage(Constants.VerifyConnection, string.Empty);
            var payload = Encoding.UTF8.GetBytes(socketMessage.ToJsonString() + "\r\n");
            return payload;
        }

        public interface IConnectionListener
        {
            void OnConnectionResult(bool isConnnected);
        }
    }
}