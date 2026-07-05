namespace MusicBeePlugin.Services.Core
{
    /// <summary>
    ///     Interface for broadcasting events to clients
    /// </summary>
    public interface IBroadcaster
    {
        /// <summary>
        ///     Broadcast cover data to all clients
        /// </summary>
        /// <param name="cover">Base64 encoded cover data</param>
        void BroadcastCover(string cover);

        /// <summary>
        ///     Broadcast lyrics to all clients
        /// </summary>
        /// <param name="lyrics">Lyrics text</param>
        void BroadcastLyrics(string lyrics);
    }
}
