using System.Runtime.Serialization;

namespace MusicBeePlugin.Enumerations
{
    /// <summary>
    ///     Repeat mode enumeration with PascalCase string values for backward compatibility.
    /// </summary>
    public enum RepeatMode
    {
        /// <summary>
        ///     Undefined or unknown repeat mode
        /// </summary>
        [EnumMember(Value = "Undefined")] Undefined,

        /// <summary>
        ///     No repeat - play through playlist once
        /// </summary>
        [EnumMember(Value = "None")] None,

        /// <summary>
        ///     Repeat all tracks in playlist
        /// </summary>
        [EnumMember(Value = "All")] All,

        /// <summary>
        ///     Repeat current track only
        /// </summary>
        [EnumMember(Value = "One")] One
    }
}
