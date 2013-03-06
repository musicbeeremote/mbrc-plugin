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
        public const string NowPlayingTrackRemove = "playNowRemoveSelected";

        #endregion

        #region Protocol 1.3

        public const string AutoDj = "autodj";
        public const string NowPlayingListChanged = "nowplayingchanged";
        public const string PluginVersion = "pluginversion";
        public const string LfmLoveRating = "lfmloverating";
        public const string PlaylistList = "playlistList";
        public const string NowPlayingMoveTrack = "nowplayingmove";
        public const string NowPlayingSearch = "nowplayingsearch";
        public const string LibrarySearch = "libsearch";
        public const string NowPlayingQueue = "npqueue";
        public const string Position = "position"; //Under npqueue
        public const string Info = "info"; //under npqueue
        public const string Tag = "tag"; 

        #endregion

        #region Protocol 2. JSON based

        public const string NowPlayingList = "nowplayinglist";
        public const string NowPlayingTrack = "nowplayingtrack";
        public const string NowPlayingPlay = "nowplayingplay";
        public const string NowPlayingPosition = "nowplayingposition";
        public const string NowPlayingRemove = "nowplayingremove";

        public const string LibrarySearchArtist = "librarysearchartist";
        public const string LibrarySearchAlbum = "librarysearchalbum";
        public const string LibrarySearchGenre = "librarysearchgenre";
        public const string LibrarySearchTitle = "librarysearchtitle";

        public const string LibraryArtistAlbums = "libraryartistalbums";
        public const string LibraryGenreArtists = "librarygenreartists";
        public const string LibraryAlbumTracks = "libraryalbumtracks";
        
        public const string LibraryQueueGenre = "libraryqueuegenre";
        public const string LibraryQueueArtist = "libraryqueueartist";
        public const string LibraryQueueAlbum = "libraryqueuealbum";
        public const string LibraryQueueTrack = "libraryqueuetrack";

        #endregion

        #region SocketMessage Types

        public const string Request = "req";
        public const string Reply = "rep";
        public const string Message = "msg";

        #endregion


    }
}