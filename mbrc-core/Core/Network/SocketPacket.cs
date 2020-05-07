using System.Net.Sockets;
using System.Text;

namespace MusicBeeRemote.Core.Network
{
    /// <summary>
    /// Essential information Of a socket connection with a client.
    /// </summary>
    public class SocketPacket
    {
        private readonly byte[] _dataBuffer = new byte[1024];

        /// <summary>
        /// Initializes a new instance of the <see cref="SocketPacket"/> class.
        /// </summary>
        /// <param name="workerSocket">The worker socket responsible for this packet.</param>
        /// <param name="connectionId">The connection id of the connection sending the packet.</param>
        public SocketPacket(Socket workerSocket, string connectionId)
        {
            WorkerSocket = workerSocket;
            ConnectionId = connectionId;
            Partial = new StringBuilder();
        }

        /// <summary>
        /// Gets the actual worker socket for this client connection.
        /// </summary>
        public Socket WorkerSocket { get; }

        /// <summary>
        /// Gets the identifier of the current workerSocket connection.
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        /// Gets the builder used to buffer requests from a client that are greater than the buffer size.
        /// </summary>
        public StringBuilder Partial { get; }

        /// <summary>
        /// Gets the Buffer to store the data sent by the client.
        /// </summary>
        /// <returns>The buffer byte array.</returns>
        public byte[] GetDataBuffer()
        {
            return _dataBuffer;
        }
    }
}
