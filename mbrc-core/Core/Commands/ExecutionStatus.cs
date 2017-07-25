using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Commands
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