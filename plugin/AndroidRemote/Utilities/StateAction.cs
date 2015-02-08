namespace MusicBeePlugin.AndroidRemote.Utilities
{
    /// <summary>
    /// Represents a state action that has to do with the state of player functionality such as repeat,shuffle
    /// scrobble, mute etc.
    /// </summary>
    public enum StateAction
    {
        /// <summary>
        /// Represents the change to the opposite of the current state.
        /// </summary>
        Toggle,
        /// <summary>
        /// Represent a request for the current state.
        /// </summary>
        State
    }
}
