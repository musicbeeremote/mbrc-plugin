namespace MusicBeeRemote.Core
{
    public interface IMusicBeeRemotePlugin
    {
        /// <summary>
        /// Starts the plugin socket server and API monitoring.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the plugin socket server and API monitoring.
        /// </summary>
        void Terminate();

        /// <summary>
        /// Shows the MusicBee Remote configuration panel.
        /// </summary>
        void DisplayInfoWindow();

        void NotifyTrackChanged();

        void NotifyVolumeLevelChanged();

        void NotifyVolumeMuteChanged();

        void NotifyPlayStateChanged();

        void NotifyLyricsReady();

        void NotifyArtworkReady();

        void NotifyNowPlayingListChanged();

        /// <summary>
        /// Displays the party mode configuration window.
        /// </summary>
        void DisplayPartyModeWindow();
    }
}
