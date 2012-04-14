using System.Net.Sockets;

namespace MusicBeePlugin.Networking
{
    public class SocketPacket
    {
        // Constructor which takes a Socket and a client number
        public SocketPacket(Socket socket, int clientNumber)
        {
            MCurrentSocket = socket;
            MClientNumber = clientNumber;
        }

        public Socket MCurrentSocket { get; private set; }

        public int MClientNumber { get; private set; }

        // Buffer to store the data sent by the client
        private byte[] _dataBuffer = new byte[1024];
        public byte[] DataBuffer { get { return _dataBuffer; } set { _dataBuffer = value; } }
    }
}
