namespace MusicBeePlugin.AndroidRemote.Networking
{
    /// <summary>
    /// This class represents a socket client and keeps essential information abou the specific client.
    /// The information consists of the client id, the number of packets send by the client (to handle the authenticication).
    /// And a flag representing the authentication status of the client.
    /// </summary>
    public class SocketClient
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientId"></param>
        public SocketClient(string clientId)
        {
            ClientId = clientId;
            PacketNumber = 0;
        }

        /// <summary>
        /// Represents the socket client's id.
        /// </summary>
        public string ClientId { get; private set; }

        /// <summary>
        /// Represents the number of the packets received by the client.
        /// </summary>
        public int PacketNumber { get; private set; }

        /// <summary>
        /// This property represents the authentication status of the specified client. If false the client should not receive any broadcast
        /// of status changes on the player.
        /// </summary>
        public bool Authenticated { get; set; }

        /// <summary>
        /// Function used to increase the number of packages received by the specific client.
        /// </summary>
        public void IncreasePacketNumber()
        {
            if (PacketNumber >= 0 && PacketNumber < 40)
                PacketNumber++;
            Authenticated = (PacketNumber >= 2);
        }
    }
}
