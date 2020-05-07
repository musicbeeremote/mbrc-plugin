using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Enumerations
{
    public enum ShuffleState
    {
        /// <summary>
        /// Shuffle is deactivated.
        /// </summary>
        [EnumMember(Value = "off")]
        Off,

        /// <summary>
        /// Shuffle is activated.
        /// </summary>
        [EnumMember(Value = "shuffle")]
        Shuffle,

        /// <summary>
        /// AutoDJ mode is activated.
        /// </summary>
        [EnumMember(Value = "autodj")]
        Autodj,
    }
}
