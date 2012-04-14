namespace MusicBeePlugin.Networking
{
    public class SocketClient
    {
        public SocketClient(int clientId)
        {
            ClientId = clientId;
            PacketNumber = 0;
        }

        public int ClientId { get; private set; }
        public int PacketNumber { get; private set; }
        public bool Authenticated { get; set; }

        public void IncreasePacketNumber()
        {
            if (PacketNumber >= 0 && PacketNumber < 40)
                PacketNumber++;
        }
    }
}
