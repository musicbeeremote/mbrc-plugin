namespace MusicBeePlugin.Networking.Server
{
    /// <summary>
    ///     This class represents a socket client and keeps essential information about the specific client.
    ///     The information consists of the connection id, the number of packets send by the client (to handle the
    ///     authentication).
    ///     And a flag representing the authentication status of the client.
    /// </summary>
    public class SocketClient
    {
        /// <summary>
        /// </summary>
        /// <param name="connectionId"></param>
        public SocketClient(string connectionId)
        {
            ConnectionId = connectionId;
            PacketNumber = 0;
            ClientProtocolVersion = 2;
            ClientPlatform = ClientOS.Unknown;
            BroadcastsEnabled = true;
            Capabilities = new ClientCapabilities();
        }

        /// <summary>
        ///     Represents the socket client's connection id.
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        ///     Represents the client-provided identifier from the protocol message.
        ///     This is set when the client sends a client_id field in the protocol message.
        /// </summary>
        public string ClientId { get; set; }

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

        /// <summary>
        /// Represents if the client will receive broadcast actions from the service (like volume updates etc).
        /// This property is by default enabled. If disable the client will receive only responses to specific requests.
        /// </summary>
        public bool BroadcastsEnabled { get; set; }

        /// <summary>
        /// Protocol capabilities supported by this client based on their protocol version.
        /// </summary>
        public ClientCapabilities Capabilities { get; private set; }

        /// <summary>
        /// Sets the client capabilities based on the raw protocol version.
        /// </summary>
        /// <param name="rawVersion">The raw version (e.g., 2, 2.1, 2.2, 3, 4)</param>
        public void SetCapabilitiesFromVersion(double rawVersion)
        {
            Capabilities = ClientCapabilities.FromVersion(rawVersion);
        }

        /// <summary>
        ///     Function used to increase the number of packages received by the specific client.
        /// </summary>
        public void IncreasePacketNumber()
        {
            if (PacketNumber >= 0 && PacketNumber < 40)
                PacketNumber++;
            Authenticated = PacketNumber >= 2;
        }

        /// <summary>
        ///     Returns a string representation of the SocketClient.
        /// </summary>
        /// <returns>A string containing the client's details.</returns>
        public override string ToString()
        {
            var clientIdPart = !string.IsNullOrEmpty(ClientId) ? $", ClientId={ClientId}" : "";
            return
                $"SocketClient[Id={ConnectionId}{clientIdPart}, Platform={ClientPlatform}, Protocol=v{ClientProtocolVersion}, " +
                $"Authenticated={Authenticated}, Broadcasts={BroadcastsEnabled}, Packets={PacketNumber}]";
        }

        /// <summary>
        ///     Returns a short identifier for logging purposes.
        ///     Format: "connId[0:6]:clientId[0:6]" or just "connId[0:6]" if no ClientId.
        /// </summary>
        public string ShortId
        {
            get
            {
                var connPart = ConnectionId?.Length > 6 ? ConnectionId.Substring(0, 6) : ConnectionId ?? "?";
                if (string.IsNullOrEmpty(ClientId))
                    return connPart;
                var clientPart = ClientId.Length > 6 ? ClientId.Substring(0, 6) : ClientId;
                return $"{connPart}:{clientPart}";
            }
        }
    }
}
