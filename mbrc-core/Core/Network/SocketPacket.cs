using System.Net.Sockets;
using System.Text;

namespace MusicBeeRemote.Core.Network
{
    /// <summary>
    /// Essential information Of a socket connection with a client.
    /// </summary>
    public class SocketPacket
    {
        /// <summary>
        /// Constructor which takes a Socket and a client number
        /// </summary>
        /// <param name="workerSocket"></param>
        /// <param name="connectionId"> </param>
        public SocketPacket(Socket workerSocket, string connectionId)
        {
            WorkerSocket = workerSocket;
            ConnectionId = connectionId;
            Partial = new StringBuilder();
        }

        /// <summary>
        /// The actual worker socket for this client connection
        /// </summary>
        public Socket WorkerSocket { get; }

        /// <summary>
        /// The identifier of the current workerSocket connection
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        /// Buffer to store the data sent by the client
        /// </summary>
        public byte[] DataBuffer { get; set; } = new byte[1024];

        /// <summary>
        /// Used to buffer requests from a client that are greter than the buffer size
        /// </summary>
        public StringBuilder Partial { get; }
    }
}