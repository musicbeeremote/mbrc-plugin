namespace MusicBeePlugin.Constants
{
    /// <summary>
    ///     Network-related constants for socket server configuration.
    /// </summary>
    internal static class NetworkConstants
    {
        /// <summary>
        ///     Socket listen backlog size (number of pending connections allowed).
        /// </summary>
        public const int SocketBacklogSize = 10;

        /// <summary>
        ///     Interval in milliseconds between ping messages to clients.
        /// </summary>
        public const int PingIntervalMs = 15000;

        /// <summary>
        ///     Delay in milliseconds when restarting the socket server.
        /// </summary>
        public const int SocketRestartDelayMs = 100;

        /// <summary>
        ///     Socket error code for "Connection reset by peer" (WSAECONNRESET).
        /// </summary>
        public const int SocketErrorConnectionReset = 10054;

        /// <summary>
        ///     Socket error code for "Software caused connection abort" (WSAECONNABORTED).
        /// </summary>
        public const int SocketErrorConnectionAborted = 10053;

        /// <summary>
        ///     Client identifier used for broadcasting to all clients.
        /// </summary>
        public const string BroadcastAllClientId = "all";
    }
}
