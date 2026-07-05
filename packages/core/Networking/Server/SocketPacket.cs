using System.Net.Sockets;
using System.Text;

namespace MusicBeePlugin.Networking.Server
{
    /// <summary>
    /// Represents a data packet used for asynchronous socket communication.
    /// </summary>
    public class SocketPacket
    {
        // Constructor which takes a Socket and a client number
        /// <summary>
        /// Represents a data packet class used in asynchronous socket communication,
        /// encapsulating details about the socket connection, client identifier, and buffer management.
        /// </summary>
        public SocketPacket(Socket socket, string connectionId)
        {
            MCurrentSocket = socket;
            ConnectionId = connectionId;
            Partial = new StringBuilder();
        }

        /// <summary>
        /// Gets the current socket associated with the connection. This socket is used for asynchronous
        /// communication between the server and the client.
        /// </summary>
        public Socket MCurrentSocket { get; private set; }

        /// <summary>
        /// Gets the unique identifier for the connection. This identifier is
        /// used to associate data and communication with the appropriate client.
        /// </summary>
        public string ConnectionId { get; private set; }

        /// <summary>
        /// Gets the buffer used to store data received from the client during
        /// asynchronous socket communication. The buffer size is pre-defined to
        /// handle incoming data efficiently.
        /// </summary>
        public byte[] DataBuffer { get; } = new byte[1024];

        public StringBuilder Partial { get; private set; }
    }
}
