namespace MusicBeePlugin.Services.UI
{
    public interface IWindowManager
    {
        void OpenInfoWindow();

        /// <summary>
        ///     Updates the socket connection status display in the info window.
        /// </summary>
        /// <param name="status">True if socket server is running, false otherwise</param>
        void UpdateWindowStatus(bool status);
    }
}
