using System;

namespace MusicBeePlugin.Adapters.Contracts
{
    /// <summary>
    ///     Interface for MusicBee system operations.
    ///     Handles background tasks and window access.
    ///     Unlike data providers, this interface provides operations rather than data retrieval.
    /// </summary>
    public interface ISystemOperations
    {
        /// <summary>
        ///     Creates a background task that runs on a separate thread.
        /// </summary>
        /// <param name="task">The action to execute in the background</param>
        void CreateBackgroundTask(Action task);

        /// <summary>
        ///     Sets the message displayed for the currently running background task.
        /// </summary>
        /// <param name="message">The progress message to display</param>
        void SetBackgroundTaskMessage(string message);

        /// <summary>
        ///     Gets the handle to the main MusicBee window.
        /// </summary>
        /// <returns>Window handle</returns>
        IntPtr GetWindowHandle();
    }
}
