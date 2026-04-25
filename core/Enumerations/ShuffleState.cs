using System.Runtime.Serialization;

namespace MusicBeePlugin.Enumerations
{
    /// <summary>
    ///     Shuffle state enumeration.
    /// </summary>
    public enum ShuffleState
    {
        [EnumMember(Value = "off")] Off,
        [EnumMember(Value = "shuffle")] Shuffle,
        [EnumMember(Value = "autodj")] AutoDj
    }
}
