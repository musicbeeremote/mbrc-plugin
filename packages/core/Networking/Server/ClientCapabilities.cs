namespace MusicBeePlugin.Networking.Server
{
    /// <summary>
    /// Represents the protocol capabilities supported by a client.
    /// Capabilities are determined during protocol handshake based on the client's version.
    /// </summary>
    public class ClientCapabilities
    {
        /// <summary>
        /// Client supports payload wrapper objects (CoverPayload, LyricsPayload).
        /// Introduced in protocol 2.1.
        /// </summary>
        public bool SupportsPayloadObjects { get; set; }

        /// <summary>
        /// Client supports paginated responses for large data sets.
        /// Introduced in protocol 2.2.
        /// </summary>
        public bool SupportsPagination { get; set; }

        /// <summary>
        /// Client supports AutoDJ shuffle state format.
        /// Introduced in protocol 2.1.
        /// </summary>
        public bool SupportsAutoDjShuffle { get; set; }

        /// <summary>
        /// Client supports full player status with ShuffleState.
        /// Introduced in protocol 2.1.
        /// </summary>
        public bool SupportsFullPlayerStatus { get; set; }

        /// <summary>
        /// Creates default capabilities (none enabled).
        /// </summary>
        public ClientCapabilities()
        {
        }

        /// <summary>
        /// Creates capabilities based on the raw protocol version.
        /// </summary>
        /// <param name="rawVersion">The original version value (e.g., 2, 2.1, 2.2, 3, 4)</param>
        public static ClientCapabilities FromVersion(double rawVersion)
        {
            var capabilities = new ClientCapabilities();

            // V2.1+ features (includes V3, V4)
            if (rawVersion >= 2.1)
            {
                capabilities.SupportsPayloadObjects = true;
                capabilities.SupportsAutoDjShuffle = true;
                capabilities.SupportsFullPlayerStatus = true;
            }

            // V2.2+ features (includes V3, V4)
            if (rawVersion >= 2.2)
            {
                capabilities.SupportsPagination = true;
            }

            return capabilities;
        }

        /// <summary>
        /// Creates capabilities from an integer version.
        /// </summary>
        public static ClientCapabilities FromVersion(int version)
        {
            return FromVersion((double)version);
        }

        public override string ToString()
        {
            return $"Capabilities[Payload={SupportsPayloadObjects}, Pagination={SupportsPagination}, " +
                   $"AutoDJ={SupportsAutoDjShuffle}, FullStatus={SupportsFullPlayerStatus}]";
        }
    }
}
