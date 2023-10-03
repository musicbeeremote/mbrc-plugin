using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Enumerations
{
    internal enum ShuffleState
    {
        [EnumMember(Value = "off")]
        Off,
        [EnumMember(Value = "shuffle")]
        Shuffle,
        [EnumMember(Value = "autodj")]
        AutoDj
    }
}