namespace MusicBeePlugin.AndroidRemote.Enumerations
{
    /// <summary>
    /// Represents the player's play state
    /// </summary>
    public enum PlayerState
    {
        /// <summary>
        /// Represents an undefined state.
        /// </summary>
        Undefined,
        /// <summary>
        /// Represents a new track loading state.
        /// </summary>
        Loading,
        /// <summary>
        /// Represents a track playing state.
        /// </summary>
        Playing,
        /// <summary>
        /// Represents a track being paused state.
        /// </summary>
        Paused,
        /// <summary>
        /// Represents a track being stopped state.
        /// </summary>
        Stopped
    }
}