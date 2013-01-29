namespace MusicBeePlugin.AndroidRemote.Networking
{
    internal static class Constants
    {
        #region Protocol 1

        public const string PlayPause = "playPause";
        public const string Previous = "previous";
        public const string Next = "next";
        public const string Stop = "stopPlayback";
        public const string PlayState = "playState";
        public const string Volume = "volume";
        public const string SongInformation = "songInfo";
        public const string SongCover = "songCover";
        public const string Shuffle = "shuffle";
        public const string Mute = "mute";
        public const string Repeat = "repeat";
        public const string Playlist = "playlist";
        public const string PlayNow = "playNow";
        public const string Scrobble = "scrobbler";
        public const string Lyrics = "lyrics";
        public const string Rating = "rating";
        public const string PlayerStatus = "playerStatus";
        public const string Error = "error";
        public const string Artist = "artist";
        public const string Title = "title";
        public const string Album = "album";
        public const string Year = "year";
        public const string Protocol = "protocol";
        public const string Player = "player";
        public const string ProtocolVersion = "1.3";
        public const string PlayerName = "MusicBee";
        public const string PlaylistItem = "playlistItem";

        #endregion

        #region Protocol 1.2

        public const string PlaybackPosition = "playbackPosition";
        public const string NowPlayingTrackRemoval = "playNowRemoveSelected";

        #endregion

        #region Protocol 1.3

        public const string AutoDj = "autodj";
        public const string NowPlayingListChanged = "nowplayingchanged";
        public const string PluginVersion = "pluginversion";
        public const string LfmLoveRating = "lfmloverating";
        public const string PlaylistList = "playlistList";
        public const string NowPlayingMoveTrack = "nowplayingmove";
        
        #endregion

        

    }
}