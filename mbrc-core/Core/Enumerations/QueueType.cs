namespace MusicBeeRemote.Core.Enumerations
{
    public enum QueueType
    {
        /// <summary>
        /// Adds the track at the end of the Queue
        /// </summary>
        Last,

        /// <summary>
        /// Adds the track after the current track.
        /// </summary>
        Next,

        /// <summary>
        /// Clears the list and plays the track.
        /// </summary>
        PlayNow,

        /// <summary>
        /// Adds the tracks in the list and plays the one specified.
        /// </summary>
        AddAndPlay,
    }
}
