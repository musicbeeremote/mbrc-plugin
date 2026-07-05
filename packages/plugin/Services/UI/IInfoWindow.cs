namespace MusicBeePlugin.Services.UI
{
    /// <summary>
    ///     Interface for the plugin information and settings window.
    ///     Provides methods for displaying and updating the window state.
    /// </summary>
    public interface IInfoWindow
    {
        /// <summary>
        ///     Gets whether the window is currently visible.
        /// </summary>
        bool Visible { get; }

        /// <summary>
        ///     Shows the information window.
        /// </summary>
        void Show();

        /// <summary>
        ///     Updates the socket connection status display.
        /// </summary>
        /// <param name="isRunning">True if socket server is running, false otherwise</param>
        void UpdateSocketStatus(bool isRunning);

        /// <summary>
        ///     Updates the cover cache status display.
        /// </summary>
        /// <param name="cached">Current cache status message</param>
        void UpdateCacheState(string cached);
    }
}
