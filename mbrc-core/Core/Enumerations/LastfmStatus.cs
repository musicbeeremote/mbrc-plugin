using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Enumerations
{
    public enum LastfmStatus
    {
        [EnumMember(Value = "normal")]
        Normal,
        [EnumMember(Value = "Love")]
        Love,
        [EnumMember(Value = "Ban")]
        Ban
    }
}
