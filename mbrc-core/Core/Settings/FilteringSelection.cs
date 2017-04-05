using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Settings
{
    /// <summary>
    /// Represents the available IP address filterin options.
    /// </summary>
    public enum FilteringSelection
    {
        /// <summary>
        /// When selected every single IP address while be allowed to connect.
        /// </summary>
        [EnumMember(Value = "all")]
        All,
        /// <summary>
        /// When selected only the IP addresses inside a specific range will be allowed to connect.
        /// </summary>
        [EnumMember(Value = "range")]
        Range,
        /// <summary>
        /// When selected only the specified IP addresses will be allowed to connect.
        /// </summary>
        [EnumMember(Value = "specific")]
        Specific
    }
}