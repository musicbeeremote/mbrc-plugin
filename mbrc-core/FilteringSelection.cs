namespace MusicBeeRemoteCore
{
    /// <summary>
    /// Represents the available IP address filterin options.
    /// </summary>
    public enum FilteringSelection
    {
        /// <summary>
        /// When selected every single IP address while be allowed to connect.
        /// </summary>
        All,
        /// <summary>
        /// When selected only the IP addresses inside a specific range will be allowed to connect.
        /// </summary>
        Range,
        /// <summary>
        /// When selected only the specified IP addresses will be allowed to connect.
        /// </summary>
        Specific
    }
}