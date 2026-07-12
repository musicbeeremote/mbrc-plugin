using MusicBeePlugin.Providers;

namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Implementation for MusicBee system operations.
    ///     Provides access to background task progress messaging.
    /// </summary>
    public class SystemOperations : ISystemOperations
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        public SystemOperations(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <summary>
        ///     Sets the message displayed for the currently running background task.
        /// </summary>
        /// <param name="message">The progress message to display</param>
        public void SetBackgroundTaskMessage(string message)
        {
            _api.MB_SetBackgroundTaskMessage(message);
        }
    }
}
