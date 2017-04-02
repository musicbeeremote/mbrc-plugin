using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Enumerations
{
    /// <summary>
    /// Represents the player's play state
    /// </summary>
    public enum PlayerState
    {
        /// <summary>
        /// Represents an undefined state.
        /// </summary>
        [EnumMember(Value = "undefined")]
        Undefined,
        /// <summary>
        /// Represents a new track loading state.
        /// </summary>
        [EnumMember(Value = "loading")]
        Loading,
        /// <summary>
        /// Represents a track playing state.
        /// </summary>
        [EnumMember(Value = "playing")]
        Playing,
        /// <summary>
        /// Represents a track being paused state.
        /// </summary>
        [EnumMember(Value = "paused")]
        Paused,
        /// <summary>
        /// Represents a track being stopped state.
        /// </summary>
        [EnumMember(Value = "stopped")]
        Stopped
    }
}