using System.Runtime.Serialization;

namespace MusicBeePlugin.Enumerations
{
    /// <summary>
    ///     Shuffle state enumeration. StringEnumConverter is applied globally via SocketMessage.SerializerSettings.
    /// </summary>
    public enum ShuffleState
    {
        [EnumMember(Value = "off")] Off,
        [EnumMember(Value = "shuffle")] Shuffle,
        [EnumMember(Value = "autodj")] AutoDj
    }
}
