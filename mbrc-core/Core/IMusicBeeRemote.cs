namespace MusicBeeRemote.Core
{
    public interface IMusicBeeRemote
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
    }
}