using System.Runtime.Serialization;

namespace MusicBeeRemote.PartyMode.Core.Model
{
    public enum ExecutionStatus
    {
        [EnumMember(Value = "denied")]
        Denied,
        [EnumMember(Value = "partially")]
        Partially,
        [EnumMember(Value = "executed")]
        Executed
    }
}