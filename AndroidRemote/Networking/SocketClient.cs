namespace MusicBeePlugin.AndroidRemote.Networking
{
    /// <summary>
    /// 
    /// </summary>
    public class SocketClient
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientId"></param>
        public SocketClient(int clientId)
        {
            ClientId = clientId;
            PacketNumber = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        public int ClientId { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        public int PacketNumber { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        public bool Authenticated { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public void IncreasePacketNumber()
        {
            if (PacketNumber >= 0 && PacketNumber < 40)
                PacketNumber++;
        }
    }
}
