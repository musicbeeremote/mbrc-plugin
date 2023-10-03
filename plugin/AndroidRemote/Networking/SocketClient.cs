﻿namespace MusicBeePlugin.AndroidRemote.Networking
{
    /// <summary>
    ///     This class represents a socket client and keeps essential information about the specific client.
    ///     The information consists of the client id, the number of packets send by the client (to handle the
    ///     authentication).
    ///     And a flag representing the authentication status of the client.
    /// </summary>
    public class SocketClient
    {
        /// <summary>
        /// </summary>
        /// <param name="clientId"></param>
        public SocketClient(string clientId)
        {
            ClientId = clientId;
            PacketNumber = 0;
        }

        /// <summary>
        ///     Represents the socket client's id.
        /// </summary>
        public string ClientId { get; private set; }

        /// <summary>
        ///     Represents the number of the packets received by the client.
        /// </summary>
        public int PacketNumber { get; private set; }

        /// <summary>
        ///     This property represents the authentication status of the specified client. If false the client should not receive
        ///     any broadcast
        ///     of status changes on the player.
        /// </summary>
        public bool Authenticated { get; set; }

        /// <summary>
        ///     Represents the version of the protocol supported by the client.
        /// </summary>
        public int ClientProtocolVersion { get; set; }

        /// <summary>
        ///     The platform the client uses (Android/iOS/Unknown) this helps craft specific protocol responses for each different
        ///     client.
        /// </summary>
        public ClientOS ClientPlatform { get; set; }

        /// Represents if the client will receive broadcast actions from the service (like volume updates etc).
        /// This property is by default enabled. If disable the client will receive only responses to specific requests.
        /// </summary>
        public bool BroadcastsEnabled { get; set; } = true;

        /// <summary>
        ///     Function used to increase the number of packages received by the specific client.
        /// </summary>
        public void IncreasePacketNumber()
        {
            if (PacketNumber >= 0 && PacketNumber < 40)
                PacketNumber++;
            Authenticated = PacketNumber >= 2;
        }
    }
}