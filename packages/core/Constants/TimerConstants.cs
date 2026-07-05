namespace MusicBeePlugin.Constants
{
    /// <summary>
    ///     Timer-related constants for state monitoring.
    /// </summary>
    internal static class TimerConstants
    {
        /// <summary>
        ///     Interval in milliseconds for checking player state changes (shuffle, scrobble, repeat).
        /// </summary>
        public const int StateCheckIntervalMs = 1000;

        /// <summary>
        ///     Interval in milliseconds for broadcasting playback position updates.
        /// </summary>
        public const int PositionUpdateIntervalMs = 20000;
    }
}
