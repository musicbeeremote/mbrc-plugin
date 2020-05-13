using System.Net;

namespace MusicBeeRemote.Core.Network
{
    /// <summary>
    /// This class represents a socket client and keeps essential information about the specific client.
    /// The information consists of the client id, the number of packets send by the client (to handle the authentication).
    /// And a flag representing the authentication status of the client.
    /// </summary>
    public class SocketConnection
    {
        public SocketConnection(string connectionId)
        {
            ConnectionId = connectionId;
            PacketNumber = 0;
        }

        /// <summary>
        /// Gets the unique Identifier of the Socket Connection. (A client might have multiple socket connections.
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        /// Gets the number of the packets received by the client.
        /// </summary>
        public int PacketNumber { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether authentication of the specified client is complete. If false the client should not receive any broadcast
        /// of status changes on the player.
        /// </summary>
        public bool Authenticated { get; set; }

        /// <summary>
        /// Gets or sets the version of the protocol supported by the client.
        /// </summary>
        public int ClientProtocolVersion { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the client will receive broadcast actions from the service (like volume updates etc).
        /// This property is by default enabled. If disable the client will receive only responses to specific requests.
        /// </summary>
        public bool BroadcastsEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the unique identifier of the Connected client. This value is reported by the client itself.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Gets or sets The address of the connected client.
        /// </summary>
        public IPAddress IpAddress { get; set; }

        /// <summary>
        /// Function used to increase the number of packages received by the specific client.
        /// </summary>
        public void IncreasePacketNumber()
        {
            if (PacketNumber >= 0 && PacketNumber < 40)
            {
                PacketNumber++;
            }

            Authenticated = PacketNumber >= 2;
        }
    }
}
