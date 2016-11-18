using System.Text;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    using System.Net.Sockets;

    /// <summary>
    /// 
    /// </summary>
    public class SocketPacket
    {
        // Constructor which takes a Socket and a client number
        /// <summary>
        /// 
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="clientId"> </param>
        public SocketPacket(Socket socket, string clientId)
        {
            MCurrentSocket = socket;
            ClientId = clientId;
            Partial = new StringBuilder();
        }

        /// <summary>
        /// 
        /// </summary>
        public Socket MCurrentSocket { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public string ClientId { get; private set; }

        // Buffer to store the data sent by the client
        /// <summary>
        /// 
        /// </summary>
        public byte[] DataBuffer { get; set; } = new byte[1024];

        public StringBuilder Partial { get; private set; }
    }
}