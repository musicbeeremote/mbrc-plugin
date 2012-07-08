using System.Net.Sockets;

namespace AndroidRemote.Networking
{
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
        /// <param name="clientNumber"></param>
        public SocketPacket(Socket socket, int clientNumber)
        {
            MCurrentSocket = socket;
            MClientNumber = clientNumber;
        }

        /// <summary>
        /// 
        /// </summary>
        public Socket MCurrentSocket { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public int MClientNumber { get; private set; }

        // Buffer to store the data sent by the client
        private byte[] _dataBuffer = new byte[1024];
        /// <summary>
        /// 
        /// </summary>
        public byte[] DataBuffer { get { return _dataBuffer; } set { _dataBuffer = value; } }
    }
}
