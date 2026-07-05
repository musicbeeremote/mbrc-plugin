using System.Runtime.Serialization;

namespace MusicBeePlugin.Enumerations
{
    /// <summary>
    ///     Repeat mode enumeration with PascalCase string values for backward compatibility.
    ///     StringEnumConverter is applied globally via SocketMessage.SerializerSettings.
    ///     TODO: Consider normalizing to lowercase in a future protocol version update.
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
