using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MusicBeePlugin.Models.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    ///     Test client for integration testing the socket server.
    ///     Handles connection, handshake, and message exchange.
    /// </summary>
    public class SocketTestClient : IDisposable
    {
        private const int DefaultTimeoutMs = 5000;
        private readonly TcpClient _client;
        private readonly ConcurrentQueue<SocketMessage> _receivedMessages;
        private readonly SemaphoreSlim _messageSemaphore;
        private readonly CancellationTokenSource _cts;
        private NetworkStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;
        private Task _receiveTask;
        private bool _disposed;

        private SocketTestClient()
        {
            _client = new TcpClient();
            _receivedMessages = new ConcurrentQueue<SocketMessage>();
            _messageSemaphore = new SemaphoreSlim(0);
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        ///     Connects to the socket server at the specified port.
        /// </summary>
        public static async Task<SocketTestClient> ConnectAsync(int port, int timeoutMs = DefaultTimeoutMs)
        {
            var client = new SocketTestClient();

            var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await client._client.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
            }
            finally
            {
                cts.Dispose();
            }

            client._stream = client._client.GetStream();
            client._reader = new StreamReader(client._stream, Encoding.UTF8);
            // Use UTF8 without BOM to avoid BOM characters in the protocol
            client._writer = new StreamWriter(client._stream, new UTF8Encoding(false)) { AutoFlush = true };

            // Start receiving messages in background
            client._receiveTask = client.ReceiveLoopAsync(client._cts.Token);

            return client;
        }

        /// <summary>
        ///     Performs the handshake sequence (player + protocol messages).
        /// </summary>
        public async Task HandshakeAsync(string platform = "Android", int protocolVersion = 5)
        {
            // Step 1: Send player identification
            await SendRawAsync("player", platform).ConfigureAwait(false);
            await WaitForMessageAsync("player").ConfigureAwait(false);

            // Step 2: Send protocol version
            var protocolData = new { protocol_version = protocolVersion, no_broadcast = true };
            await SendRawAsync("protocol", protocolData).ConfigureAwait(false);
            await WaitForMessageAsync("protocol").ConfigureAwait(false);
        }

        /// <summary>
        ///     Sends a message and waits for the response.
        /// </summary>
        public async Task<SocketMessage> SendAndReceiveAsync(string context, object data = null, int timeoutMs = DefaultTimeoutMs)
        {
            await SendRawAsync(context, data).ConfigureAwait(false);
            return await WaitForMessageAsync(context, timeoutMs).ConfigureAwait(false);
        }

        /// <summary>
        ///     Sends a raw message to the server.
        /// </summary>
        public Task SendRawAsync(string context, object data = null)
        {
            var message = new SocketMessage(context, data ?? string.Empty);
            var json = JsonConvert.SerializeObject(message);
            // Protocol requires CRLF terminator, not platform-specific newline
            return _writer.WriteAsync(json + "\r\n");
        }

        /// <summary>
        ///     Waits for a message with the specified context.
        /// </summary>
        public async Task<SocketMessage> WaitForMessageAsync(string expectedContext = null, int timeoutMs = DefaultTimeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                // Check if we have a matching message
                if (_receivedMessages.TryDequeue(out var message))
                {
                    if (expectedContext == null || message.Context == expectedContext)
                    {
                        return message;
                    }

                    // Not the message we're looking for, put it back? Or just continue
                    // For simplicity, we'll just continue looking
                }

                // Wait for more messages
                var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0)
                {
                    break;
                }

                await _messageSemaphore.WaitAsync(Math.Min(remaining, 100)).ConfigureAwait(false);
            }

            throw new TimeoutException($"Timeout waiting for message with context '{expectedContext}'");
        }

        /// <summary>
        ///     Gets the data from a message as the specified type.
        /// </summary>
        public static T GetData<T>(SocketMessage message)
        {
            if (message.Data == null)
            {
                return default;
            }

            if (message.Data is T typed)
            {
                return typed;
            }

            if (message.Data is JToken token)
            {
                return token.ToObject<T>();
            }

            return JsonConvert.DeserializeObject<T>(message.Data.ToString());
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _client.Connected)
                {
                    // ReadLineAsync with CancellationToken not available in .NET 4.8
#pragma warning disable CA2016
                    var line = await _reader.ReadLineAsync().ConfigureAwait(false);
#pragma warning restore CA2016
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    try
                    {
                        var message = JsonConvert.DeserializeObject<SocketMessage>(line);
                        if (message != null)
                        {
                            _receivedMessages.Enqueue(message);
                            _messageSemaphore.Release();
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed messages in tests
                    }
                }
            }
            catch (Exception) when (ct.IsCancellationRequested || _disposed)
            {
                // Expected during shutdown
            }
            catch (IOException)
            {
                // Connection closed
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (disposing)
            {
                _cts.Cancel();

                try
                {
                    _writer?.Dispose();
                    _reader?.Dispose();
                    _stream?.Dispose();
                    _client?.Dispose();

                    // Wait for the receive task to complete
                    _receiveTask?.Wait(1000);
                }
                catch
                {
                    // Ignore disposal errors
                }

                _cts.Dispose();
                _messageSemaphore.Dispose();
            }
        }
    }
}
