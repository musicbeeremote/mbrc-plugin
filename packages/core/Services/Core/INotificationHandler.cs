namespace MusicBeePlugin.Services.Core
{
    /// <summary>
    ///     Interface for handling MusicBee notifications.
    ///     Abstracts the notification handling logic from Plugin.cs ReceiveNotification method.
    /// </summary>
    public interface INotificationHandler
    {
        /// <summary>
        ///     Handles track change notifications.
        ///     Updates now playing information and broadcasts track change events.
        /// </summary>
        /// <param name="sourceFileUrl">The URL of the new track</param>
        void HandleTrackChanged(string sourceFileUrl);

        /// <summary>
        ///     Handles volume level change notifications.
        ///     Broadcasts new volume level to clients.
        /// </summary>
        void HandleVolumeLevelChanged();

        /// <summary>
        ///     Handles volume mute change notifications.
        ///     Broadcasts mute state to clients.
        /// </summary>
        void HandleVolumeMuteChanged();

        /// <summary>
        ///     Handles play state change notifications.
        ///     Broadcasts new play state to clients.
        /// </summary>
        void HandlePlayStateChanged();

        /// <summary>
        ///     Handles notification when now playing lyrics are ready.
        ///     Updates lyrics data and broadcasts to clients.
        /// </summary>
        void HandleNowPlayingLyricsReady();

        /// <summary>
        ///     Handles notification when now playing artwork is ready.
        ///     Updates cover data and broadcasts to clients.
        /// </summary>
        void HandleNowPlayingArtworkReady();

        /// <summary>
        ///     Handles notification when now playing list changes.
        ///     Broadcasts list change event to clients.
        /// </summary>
        void HandleNowPlayingListChanged();

        /// <summary>
        ///     Handles notification when a file is added to the library.
        ///     Updates cover cache with new file information.
        /// </summary>
        /// <param name="sourceFileUrl">The URL of the added file</param>
        void HandleFileAddedToLibrary(string sourceFileUrl);

        /// <summary>
        ///     Broadcasts the initial now playing state including cover and lyrics.
        ///     Should be called during plugin initialization to sync client state.
        /// </summary>
        void BroadcastInitialNowPlayingState();
    }
}
