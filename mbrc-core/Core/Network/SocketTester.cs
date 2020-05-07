using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Settings;
using Newtonsoft.Json.Linq;
using NLog;

namespace MusicBeeRemote.Core.Network
{
    public class SocketTester
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly PersistenceManager _settings;

        public SocketTester(PersistenceManager settings)
        {
            _settings = settings;
        }

        public delegate void ConnectionStatusHandler(bool connectionStatus);

        public event ConnectionStatusHandler ConnectionChangeListener;

        /// <summary>
        /// Starts a local socket client that attempts to connect to the plugin
        /// to verify that the socket server is running properly.
        /// </summary>
        public void VerifyConnection()
        {
            try
            {
                var port = _settings.UserSettingsModel.ListeningPort;
                var ipEndpoint = new IPEndPoint(IPAddress.Loopback, (int)port);
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                client.BeginConnect(ipEndpoint, ConnectCallback, client);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Debug, e, "Tester Connection error");
                OnConnectionChange(false);
            }
        }

        protected virtual void OnConnectionChange(bool connectionStatus)
        {
            ConnectionChangeListener?.Invoke(connectionStatus);
        }

        private static byte[] Payload()
        {
            var socketMessage = new SocketMessage(Constants.VerifyConnection, string.Empty);
            var payload = Encoding.UTF8.GetBytes(socketMessage.ToJsonString() + "\r\n");
            return payload;
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                var state = (StateObject)ar.AsyncState;
                var client = state.GetSocket();
                var received = client.EndReceive(ar);
                var chars = new char[received + 1];
                var decoder = Encoding.UTF8.GetDecoder();
                decoder.GetChars(state.GetBuffer(), 0, received, chars, 0);
                var message = new string(chars);
                var json = JObject.Parse(message);
                var verified = (string)json["context"] == Constants.VerifyConnection;

                _logger.Log(LogLevel.Info, $"Connection verified: {verified}");
                OnConnectionChange(verified);

                client.Shutdown(SocketShutdown.Both);
                client.Dispose();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Debug, e, "Tester Connection error");
                OnConnectionChange(false);
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                var client = (Socket)ar.AsyncState;
                client.EndConnect(ar);
                var state = new StateObject(client);
                client.BeginReceive(state.GetBuffer(), 0, StateObject.BufferSize, 0, ReceiveCallback, state);
                client.ReceiveTimeout = 3000;
                client.Send(Payload());
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Debug, e, "Tester Connection error");
                OnConnectionChange(false);
            }
        }

        // State object for receiving data from remote device.
        private class StateObject
        {
            // Size of receive buffer.
            public const int BufferSize = 256;

            // Receive buffer.
            private readonly byte[] _buffer = new byte[BufferSize];

            // Client socket.
            private readonly Socket _socket;

            public StateObject(Socket socket)
            {
                _socket = socket;
            }

            public byte[] GetBuffer()
            {
                return _buffer;
            }

            public Socket GetSocket()
            {
                return _socket;
            }
        }
    }
}
