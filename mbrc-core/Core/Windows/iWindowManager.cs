namespace MusicBeeRemote.Core.Windows
{
    /// <summary>
    /// Creates and displays any dialogs that are shown by the plugin.
    /// </summary>
    public interface IWindowManager
    {
        /// <summary>
        /// Displays the information window / configuration panel for the 
        /// remote application.
        /// </summary>
        void DisplayInfoWindow();

        /// <summary>
        /// Displays the party mode configuration window.
        /// </summary>
        void DisplayPartyModeWindow();
    }
}