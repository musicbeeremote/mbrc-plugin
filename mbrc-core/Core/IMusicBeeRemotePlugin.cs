namespace MusicBeeRemote.Core
{
    public interface IMusicBeeRemotePlugin
    {
        void Start();

        void Stop();

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