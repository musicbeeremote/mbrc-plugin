namespace MusicBeePlugin.AndroidRemote.Events
{
    class EventType
    {
        // These event types represent the model data changes
        public const string ModelDataCoverChanged = "ModelDataCoverChanged";
        public const string ModelDataLyricsChanged = "ModelDataLyricsChanged";
        // Extra status events 
        public const string ActionClientConnected = "ActionClientConnected";
        public const string ActionClientDisconnected = "ActionClientDisconnected";
        public const string ActionForceClientDisconnect = "ActionForceClientDisconnect";
        public const string ActionDataAvailable = "ActionDataAvailable";
        public const string ActionSocketStart = "ActionSocketStart";
        public const string ActionSocketStop = "ActionSocketStop";
        public const string InitializeModel = "InitializeModel";
        // PlayerState thingies
        public const string PlayerStateShuffleChanged = "PlayerStateShuffleChanged";
        public const string PlayerStateScrobbleChanged = "PlayerStateScrobbleChanged";
        public const string PlayerStateRepeatChanged = "PlayerStateRepeatChanged";
        public const string PlayerStateTrackChanged = "PlayerStateTrackChanged";
        public const string PlayerStateVolumeChanged = "PlayerStateVolumeChanged";
        public const string PlayerStatePlayStateChanged = "PlayerStatePlayStateChanged";
        public const string PlayerStateLyricsChanged = "PlayerStateLyricsChanged";
        public const string PlayerStateCoverChanged = "PlayerStateCoverChanged";
        public const string PlayerStateNowPlayingListChanged = "PlayerStateNowPlayingListChanged";
        public const string PlayerStatePlaybackPositionChanged = "PlayerStatePlaybackPositionChanged";
        public const string PlayerStateAutoDjChanged = "PlayerStateAutoDjChanged";
        public const string PlayerStateMuteChanged = "PlayerStateMuteChanged";
        public const string PlayerStateStatus = "PlayerStateStatus";
        public const string PlayerStateNowPlayingListData = "PlayerStateNowPlayingListData";
        public const string PlayerStateNowPlayingTrackRemoved = "PlayerStateNowPlayingTrackRemoved";
        public const string PlayerStateLfmLoveRatingChanged = "PlayerStateLfmLoveRatingChanged";
        public const string PlayerStateRatingChanged = "PlayerStateRatingChanged";
        public const string LibraryArtistListReady = "LibraryArtistListReady";
    }
}
