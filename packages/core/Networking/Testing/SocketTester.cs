using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Protocol.Messages;
using MusicBeePlugin.Services.Configuration;
using MusicBeePlugin.Utilities.Network;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin.Networking.Testing
{
    public class SocketTester : ISocketTester
    {
        private readonly IPluginLogger _logger;
        private readonly IUserSettings _userSettings;

        public SocketTester(IUserSettings userSettings, IPluginLogger logger)
        {
            _userSettings = userSettings;
            _logger = logger;
        }

        public IConnectionListener ConnectionListener { get; set; }

        public void VerifyConnection()
        {
            try
            {
                var port = _userSettings.ListeningPort;
                var ipEndpoint = new IPEndPoint(IPAddress.Loopback, (int)port);
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                client.BeginConnect(ipEndpoint, ConnectCallback, client);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Tester Connection error: {Exception}");
                ConnectionListener?.OnConnectionResult(false);
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                var state = (StateObject)ar.AsyncState;
                var client = state.WorkSocket;
                var received = client.EndReceive(ar);
                var chars = new char[received + 1];
                var decoder = Encoding.UTF8.GetDecoder();
                decoder.GetChars(state.Buffer, 0, received, chars, 0);
                var message = new string(chars);
                var json = JObject.Parse(message);
                var verified = json["context"]?.ToString() == ProtocolConstants.VerifyConnection;

                _logger.Info($"Connection verified: {verified}");
                ConnectionListener?.OnConnectionResult(verified);

                client.Shutdown(SocketShutdown.Both);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Tester Connection error");
                ConnectionListener?.OnConnectionResult(false);
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                var client = (Socket)ar.AsyncState;
                client.EndConnect(ar);
                var state = new StateObject { WorkSocket = client };
                client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
                client.ReceiveTimeout = 3000;
                client.Send(Payload());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Tester Connection error: {Exception}");
                ConnectionListener?.OnConnectionResult(false);
            }
        }

        private static byte[] Payload()
        {
            var socketMessage = new SocketMessage(ProtocolConstants.VerifyConnection, string.Empty);
            var payload = Encoding.UTF8.GetBytes(socketMessage.ToJsonString() + ProtocolConstants.MessageTerminator);
            return payload;
        }

        // State object for receiving data from remote device.
        private sealed class StateObject
        {
            // Size of receive buffer.
            public const int BufferSize = 256;

            // Receive buffer.
            public readonly byte[] Buffer = new byte[BufferSize];

            // Received data string.
            public StringBuilder Sb = new StringBuilder();

            // Client socket.
            public Socket WorkSocket;
        }
    }
}
