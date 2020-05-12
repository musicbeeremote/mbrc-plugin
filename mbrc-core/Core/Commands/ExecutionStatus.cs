using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Commands
{
    public enum ExecutionStatus
    {
        /// <summary>
        /// The execution of the command was denied due to insufficient permissions.
        /// </summary>
        [EnumMember(Value = "denied")]
        Denied,

        /// <summary>
        /// The multi-step command was partially executed.
        /// </summary>
        [EnumMember(Value = "partially")]
        Partially,

        /// <summary>
        /// The command was fully executed.
        /// </summary>
        [EnumMember(Value = "executed")]
        Executed,
    }
}
