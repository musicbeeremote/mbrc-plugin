namespace MusicBeePlugin.Services.Core
{
    /// <summary>
    ///     Defines the contract for the plugin core initialization.
    /// </summary>
    public interface IPluginCore
    {
        void Initialize();

        /// <summary>
        ///     Enables or disables logging.
        /// </summary>
        /// <param name="enabled">True to enable logging, false to disable.</param>
        void SetLogging(bool enabled);

        /// <summary>
        ///     Starts the networking services.
        /// </summary>
        void StartNetworking();

        /// <summary>
        ///     Stops the networking services.
        /// </summary>
        void StopNetworking();

        /// <summary>
        ///     Gets the notification handler for processing MusicBee notifications.
        /// </summary>
        /// <returns>The notification handler instance</returns>
        INotificationHandler GetNotificationHandler();

        /// <summary>
        ///     Uninstalls the plugin by cleaning up resources and deleting related data folders.
        /// </summary>
        void Uninstall();

        void OpenSettingsWindow();
    }
}
