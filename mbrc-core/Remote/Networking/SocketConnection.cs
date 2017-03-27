using System.Net;

namespace MusicBeeRemoteCore.Remote.Networking
{
    /// <summary>
    /// This class represents a socket client and keeps essential information abou the specific client.
    /// The information consists of the client id, the number of packets send by the client (to handle the authenticication).
    /// And a flag representing the authentication status of the client.
    /// </summary>
    public class SocketConnection
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="connectionId"></param>
        public SocketConnection(string connectionId)
        {
            ConnectionId = connectionId;
            PacketNumber = 0;
        }

        /// <summary>
        /// Unique Identifier of the Socket Connection. (A client might have multiple socket connections
        /// </summary>
        public string ConnectionId { get; }

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
        /// Represents the version of the protocol supported by the client.
        /// </summary>
        public int ClientProtocolVersion { get; set; }

        /// <summary>
        /// Represents if the client will receive broadcast actions from the service (like volume updates etc).
        /// This property is by default enabled. If disable the client will receive only responses to specific requests.
        /// </summary>
        public bool BroadcastsEnabled { get; set; } = true;

        /// <summary>
        /// Unique identifier of the Connected client. This value is reported by the client itself.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// The address of the connected client
        /// </summary>
        public IPAddress IpAddress { get; set; }

        /// <summary>
        /// Function used to increase the number of packages received by the specific client.
        /// </summary>
        public void IncreasePacketNumber()
        {
            if (PacketNumber >= 0 && PacketNumber < 40)
                PacketNumber++;
            Authenticated = PacketNumber >= 2;
        }
    }
}