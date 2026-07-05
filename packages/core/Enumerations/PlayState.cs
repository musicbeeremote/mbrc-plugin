using System.Runtime.Serialization;

namespace MusicBeePlugin.Enumerations
{
    /// <summary>
    ///     Player state enumeration with PascalCase string values for backward compatibility.
    ///     StringEnumConverter is applied globally via SocketMessage.SerializerSettings.
    ///     TODO: Consider normalizing to lowercase in a future protocol version update.
    /// </summary>
    public enum PlayState
    {
        /// <summary>
        ///     Undefined or unknown state
        /// </summary>
        [EnumMember(Value = "Undefined")] Undefined,

        /// <summary>
        ///     Player is stopped
        /// </summary>
        [EnumMember(Value = "Stopped")] Stopped,

        /// <summary>
        ///     Player is playing
        /// </summary>
        [EnumMember(Value = "Playing")] Playing,

        /// <summary>
        ///     Player is paused
        /// </summary>
        [EnumMember(Value = "Paused")] Paused
    }
}
