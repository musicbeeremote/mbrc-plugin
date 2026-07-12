namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Interface for MusicBee system operations.
    ///     Handles background task progress messaging.
    ///     Unlike data providers, this interface provides operations rather than data retrieval.
    /// </summary>
    public interface ISystemOperations
    {
        /// <summary>
        ///     Sets the message displayed for the currently running background task.
        /// </summary>
        /// <param name="message">The progress message to display</param>
        void SetBackgroundTaskMessage(string message);
    }
}
